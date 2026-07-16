using Microsoft.Web.Administration;
using IISStateAgent.Models;
using MwaConfig = Microsoft.Web.Administration.Configuration;

namespace IISStateAgent.Services;

public class IISStateCollector
{
    private readonly ILogger<IISStateCollector> _logger;
    private readonly WebConfigReader _webConfigReader;

    public IISStateCollector(ILogger<IISStateCollector> logger, WebConfigReader webConfigReader)
    {
        _logger = logger;
        _webConfigReader = webConfigReader;
    }

    public (List<IISSiteInfo> Sites, List<IISAppPoolInfo> AppPools, List<string> Errors) Collect()
    {
        var sites = new List<IISSiteInfo>();
        var appPools = new List<IISAppPoolInfo>();
        var errors = new List<string>();

        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"inetsrv\config\applicationHost.config");
            using var mgr = new ServerManager(configPath);

            foreach (var pool in mgr.ApplicationPools)
            {
                try
                {
                    appPools.Add(MapAppPool(pool));
                }
                catch (Exception ex)
                {
                    var msg = $"Error reading app pool '{pool.Name}': {ex.Message}";
                    _logger.LogWarning(ex, msg);
                    errors.Add(msg);
                }
            }

            foreach (var site in mgr.Sites)
            {
                try
                {
                    sites.Add(MapSite(site, mgr));
                }
                catch (Exception ex)
                {
                    var msg = $"Error reading site '{site.Name}': {ex.Message}";
                    _logger.LogWarning(ex, msg);
                    errors.Add(msg);
                }
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to open IIS ServerManager: {ex.Message}";
            _logger.LogError(ex, msg);
            errors.Add(msg);
        }

        return (sites, appPools, errors);
    }

    private IISSiteInfo MapSite(Site site, ServerManager mgr)
    {
        return new IISSiteInfo
        {
            Id = site.Id,
            Name = site.Name,
            State = TryGetState(() => site.State.ToString()),
            Bindings = site.Bindings.Select(MapBinding).ToList(),
            Authentication = ReadAuthentication(mgr, site.Name, "/"),
            Ssl = ReadSsl(site, mgr),
            RequestLimits = ReadRequestLimits(mgr, site.Name, "/"),
            Applications = site.Applications
                .Select(app => TryMapApplication(app, site.Name, mgr))
                .Where(a => a != null)
                .Select(a => a!)
                .ToList(),
        };
    }

    private IISApplicationInfo? TryMapApplication(Application app, string siteName, ServerManager mgr)
    {
        try
        {
            var physicalPath = ResolvePhysicalPath(app);
            return new IISApplicationInfo
            {
                Path = app.Path,
                AppPoolName = app.ApplicationPoolName,
                PhysicalPath = physicalPath,
                Authentication = ReadAuthentication(mgr, siteName, app.Path),
                WebConfig = _webConfigReader.Read(physicalPath),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading application '{Path}' under site '{Site}'", app.Path, siteName);
            return null;
        }
    }

    private static IISAppPoolInfo MapAppPool(ApplicationPool pool)
    {
        var identity = pool.ProcessModel.IdentityType == ProcessModelIdentityType.SpecificUser
            ? pool.ProcessModel.UserName
            : pool.ProcessModel.IdentityType.ToString();

        return new IISAppPoolInfo
        {
            Name = pool.Name,
            State = TryGetState(() => pool.State.ToString()),
            Identity = identity,
            RuntimeVersion = pool.ManagedRuntimeVersion,
            PipelineMode = pool.ManagedPipelineMode.ToString(),
            StartMode = pool.StartMode.ToString(),
            IdleTimeoutMinutes = (int)pool.ProcessModel.IdleTimeout.TotalMinutes,
            AutoStart = pool.AutoStart,
            MaxWorkerProcesses = (int)pool.ProcessModel.MaxProcesses,
        };
    }

    private static BindingInfo MapBinding(Binding binding)
    {
        var parts = binding.BindingInformation.Split(':');
        return new BindingInfo
        {
            Protocol = binding.Protocol,
            IPAddress = parts.Length > 0 ? parts[0] : string.Empty,
            Port = parts.Length > 1 && int.TryParse(parts[1], out var port) ? port : 0,
            Hostname = parts.Length > 2 ? parts[2] : string.Empty,
        };
    }

    private AuthenticationInfo ReadAuthentication(ServerManager mgr, string siteName, string virtualPath)
    {
        try
        {
            var config = mgr.GetWebConfiguration(siteName, virtualPath);
            return new AuthenticationInfo
            {
                AnonymousEnabled = GetAuthEnabled(config, "anonymousAuthentication"),
                WindowsEnabled = GetAuthEnabled(config, "windowsAuthentication"),
                BasicEnabled = GetAuthEnabled(config, "basicAuthentication"),
                DigestEnabled = GetAuthEnabled(config, "digestAuthentication"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read auth config for {Site}{Path}", siteName, virtualPath);
            return new AuthenticationInfo();
        }
    }

    private static bool GetAuthEnabled(MwaConfig config, string sectionName)
    {
        try
        {
            var section = config.GetSection(
                $"system.webServer/security/authentication/{sectionName}");
            return section != null && (bool)(section["enabled"] ?? false);
        }
        catch
        {
            return false;
        }
    }

    private SslInfo ReadSsl(Site site, ServerManager mgr)
    {
        try
        {
            var config = mgr.GetWebConfiguration(site.Name, "/");
            var accessSection = config.GetSection("system.webServer/security/access");
            var sslFlags = accessSection?["sslFlags"]?.ToString() ?? string.Empty;

            var httpsBinding = site.Bindings.FirstOrDefault(b => b.Protocol == "https");
            var thumbprint = httpsBinding?.CertificateHash != null
                ? Convert.ToHexString(httpsBinding.CertificateHash).ToLower()
                : string.Empty;

            return new SslInfo
            {
                RequireSsl = sslFlags.Contains("Ssl", StringComparison.OrdinalIgnoreCase),
                ClientCertificatePolicy = sslFlags.Contains("SslRequireCert", StringComparison.OrdinalIgnoreCase) ? "Require"
                    : sslFlags.Contains("SslNegotiateCert", StringComparison.OrdinalIgnoreCase) ? "Accept"
                    : "Ignore",
                CertificateThumbprint = thumbprint,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read SSL config for site '{Site}'", site.Name);
            return new SslInfo();
        }
    }

    private RequestLimitsInfo ReadRequestLimits(ServerManager mgr, string siteName, string virtualPath)
    {
        try
        {
            var config = mgr.GetWebConfiguration(siteName, virtualPath);
            var section = config.GetSection(
                "system.webServer/security/requestFiltering/requestLimits");

            if (section == null) return new RequestLimitsInfo();

            return new RequestLimitsInfo
            {
                MaxContentLengthBytes = Convert.ToInt64(section["maxAllowedContentLength"] ?? 30000000L),
                MaxUrlLength = Convert.ToInt32(section["maxUrl"] ?? 4096),
                MaxQueryStringLength = Convert.ToInt32(section["maxQueryString"] ?? 2048),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read request limits for {Site}{Path}", siteName, virtualPath);
            return new RequestLimitsInfo();
        }
    }

    private static string TryGetState(Func<string> getter)
    {
        try { return getter(); }
        catch { return "Unknown"; }
    }

    private static string ResolvePhysicalPath(Application app)
    {
        try
        {
            var raw = app.VirtualDirectories["/"]?.PhysicalPath ?? string.Empty;
            return raw
                .Replace("%SystemDrive%", Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", StringComparison.OrdinalIgnoreCase)
                .Replace("%windir%", Environment.GetEnvironmentVariable("windir") ?? @"C:\Windows", StringComparison.OrdinalIgnoreCase)
                .Replace("%SystemRoot%", Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Empty;
        }
    }
}
