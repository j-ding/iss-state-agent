using IISStateAgent.Configuration;
using IISStateAgent.Models;
using IISStateAgent.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) =>
        config.ReadFrom.Configuration(context.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext());

    var settings = builder.Configuration
        .GetSection("AgentSettings")
        .Get<AgentSettings>() ?? new AgentSettings();
    builder.Services.AddSingleton(settings);

    var isWindowsAuth = settings.AuthenticationMode.Equals("Windows", StringComparison.OrdinalIgnoreCase);
    if (isWindowsAuth)
    {
        builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        builder.Services.AddAuthorization();
    }

    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<WebConfigReader>();
    builder.Services.AddSingleton<IISStateCollector>();
    builder.Services.AddSingleton<RuntimeDetector>();
    builder.Services.AddSingleton<WindowsEnvironmentCollector>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (isWindowsAuth)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // /health is always anonymous — monitoring tools and load balancers need it credential-free
    app.MapGet("/health", (IMemoryCache cache, AgentSettings agentSettings) =>
    {
        var hasSnapshot = cache.TryGetValue("server_snapshot", out ServerSnapshot? snapshot);

        var iisConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"inetsrv\config\applicationHost.config");
        var iisPresent = File.Exists(iisConfigPath);

        var status = iisPresent ? "healthy" : "degraded";

        return Results.Ok(new
        {
            status,
            hostname = Environment.MachineName,
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            timestamp = DateTimeOffset.UtcNow,
            iisConfigReachable = iisPresent,
            cache = new
            {
                hasSnapshot,
                snapshotTimestamp = snapshot?.Timestamp,
                ttlSeconds = agentSettings.CacheDurationSeconds,
            },
        });
    }).AllowAnonymous();

    // /state requires auth when AuthenticationMode=Windows
    var api = isWindowsAuth
        ? app.MapGroup("/").RequireAuthorization()
        : app.MapGroup("/");

    api.MapGet("/state", (
        IISStateCollector iisCollector,
        RuntimeDetector runtimeDetector,
        WindowsEnvironmentCollector envCollector,
        IMemoryCache cache,
        AgentSettings agentSettings,
        ILogger<Program> logger) =>
    {
        const string CacheKey = "server_snapshot";

        if (cache.TryGetValue(CacheKey, out ServerSnapshot? cached))
        {
            logger.LogDebug("Returning cached snapshot from {Timestamp}", cached!.Timestamp);
            return Results.Ok(cached);
        }

        logger.LogInformation("Collecting server snapshot");
        var errors = new List<string>();

        var (sites, appPools, iisErrors) = iisCollector.Collect();
        errors.AddRange(iisErrors);

        var runtimes = agentSettings.RuntimeDetection.Enabled
            ? runtimeDetector.Collect()
            : new RuntimesInfo();

        var (environment, envErrors) = envCollector.Collect();
        errors.AddRange(envErrors);

        var snapshot = new ServerSnapshot
        {
            Hostname = Environment.MachineName,
            OSVersion = Environment.OSVersion.ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            AgentVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Sites = sites,
            AppPools = appPools,
            Runtimes = runtimes,
            Environment = environment,
            Errors = errors,
        };

        cache.Set(CacheKey, snapshot, TimeSpan.FromSeconds(agentSettings.CacheDurationSeconds));

        logger.LogInformation(
            "Snapshot collected: {SiteCount} sites, {PoolCount} pools, {ErrorCount} errors",
            sites.Count, appPools.Count, errors.Count);

        return Results.Ok(snapshot);
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
