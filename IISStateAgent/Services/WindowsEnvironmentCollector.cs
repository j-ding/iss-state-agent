using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using Microsoft.Web.Administration;
using IISStateAgent.Configuration;
using IISStateAgent.Models;

namespace IISStateAgent.Services;

public class WindowsEnvironmentCollector
{
    private readonly ILogger<WindowsEnvironmentCollector> _logger;
    private readonly AgentSettings _settings;

    public WindowsEnvironmentCollector(ILogger<WindowsEnvironmentCollector> logger, AgentSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public (WindowsEnvironmentInfo Info, List<string> Errors) Collect()
    {
        var errors = new List<string>();
        var info = new WindowsEnvironmentInfo
        {
            IISModules = CollectIISModules(errors),
            MonitoredServices = CollectServices(errors),
            ScheduledTasks = CollectScheduledTasks(errors),
            HostsFileEntries = CollectHostsFile(errors),
        };
        return (info, errors);
    }

    private List<IISModuleInfo> CollectIISModules(List<string> errors)
    {
        var modules = new List<IISModuleInfo>();
        try
        {
            using var mgr = new ServerManager();
            var config = mgr.GetApplicationHostConfiguration();
            var section = config.GetSection("system.webServer/globalModules");
            var collection = section.GetCollection();

            foreach (ConfigurationElement element in collection)
            {
                modules.Add(new IISModuleInfo
                {
                    Name = element["name"]?.ToString() ?? string.Empty,
                    Image = element["image"]?.ToString() ?? string.Empty,
                });
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to read IIS global modules: {ex.Message}";
            _logger.LogWarning(ex, msg);
            errors.Add(msg);
        }
        return modules;
    }

    private List<WindowsServiceInfo> CollectServices(List<string> errors)
    {
        var results = new List<WindowsServiceInfo>();
        if (_settings.MonitoredServices.Count == 0) return results;

        try
        {
            var allServices = ServiceController.GetServices();

            foreach (var name in _settings.MonitoredServices)
            {
                var svc = allServices.FirstOrDefault(s =>
                    s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (svc == null)
                {
                    results.Add(new WindowsServiceInfo
                    {
                        Name = name,
                        DisplayName = name,
                        Status = "NotFound",
                        StartType = "Unknown",
                    });
                    continue;
                }

                results.Add(new WindowsServiceInfo
                {
                    Name = svc.ServiceName,
                    DisplayName = svc.DisplayName,
                    Status = svc.Status.ToString(),
                    StartType = svc.StartType.ToString(),
                });
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to query Windows services: {ex.Message}";
            _logger.LogWarning(ex, msg);
            errors.Add(msg);
        }

        return results;
    }

    private List<ScheduledTaskInfo> CollectScheduledTasks(List<string> errors)
    {
        var tasks = new List<ScheduledTaskInfo>();
        try
        {
            var folder = _settings.ScheduledTaskFolder.Trim('\\');
            var queryPath = string.IsNullOrEmpty(folder) ? "\\" : $"\\{folder}\\";

            var psi = new ProcessStartInfo("schtasks",
                $"/query /fo CSV /v /nh /TN \"{queryPath}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return tasks;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var task = ParseSchtasksCsvLine(line);
                if (task != null) tasks.Add(task);
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to query scheduled tasks: {ex.Message}";
            _logger.LogWarning(ex, msg);
            errors.Add(msg);
        }
        return tasks;
    }

    private static ScheduledTaskInfo? ParseSchtasksCsvLine(string line)
    {
        try
        {
            // CSV verbose columns: HostName, TaskName, Next Run Time, Status, Logon Mode,
            // Last Run Time, Last Result, Author, Task To Run, ...
            var fields = SplitCsvLine(line);
            if (fields.Count < 7) return null;

            var taskPath = fields[1].Trim('"');
            if (taskPath == "TaskName" || string.IsNullOrWhiteSpace(taskPath)) return null;

            var lastSlash = taskPath.LastIndexOf('\\');
            var taskName = lastSlash >= 0 ? taskPath[(lastSlash + 1)..] : taskPath;
            var folder = lastSlash >= 0 ? taskPath[..lastSlash] : "\\";

            return new ScheduledTaskInfo
            {
                TaskName = taskName,
                TaskPath = folder,
                NextRunTime = NullIfNA(fields[2].Trim('"')),
                State = fields[3].Trim('"'),
                LastRunTime = NullIfNA(fields[5].Trim('"')),
                LastTaskResult = fields[6].Trim('"'),
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var current = new StringBuilder(64);

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    private static string? NullIfNA(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "N/A" ? null : value;

    private List<HostsEntry> CollectHostsFile(List<string> errors)
    {
        var entries = new List<HostsEntry>();
        try
        {
            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");

            if (!File.Exists(hostsPath)) return entries;

            foreach (var line in File.ReadAllLines(hostsPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('#') || string.IsNullOrWhiteSpace(trimmed)) continue;

                var commentIndex = trimmed.IndexOf('#');
                if (commentIndex >= 0) trimmed = trimmed[..commentIndex].Trim();

                var parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    entries.Add(new HostsEntry
                    {
                        IPAddress = parts[0],
                        Hostname = parts[1],
                    });
                }
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to read hosts file: {ex.Message}";
            _logger.LogWarning(ex, msg);
            errors.Add(msg);
        }
        return entries;
    }
}
