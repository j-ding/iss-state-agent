using System.Diagnostics;
using System.Text.RegularExpressions;
using IISStateAgent.Configuration;
using IISStateAgent.Models;

namespace IISStateAgent.Services;

public partial class RuntimeDetector
{
    private readonly ILogger<RuntimeDetector> _logger;
    private readonly RuntimeDetectionSettings _settings;

    public RuntimeDetector(ILogger<RuntimeDetector> logger, AgentSettings settings)
    {
        _logger = logger;
        _settings = settings.RuntimeDetection;
    }

    public RuntimesInfo Collect()
    {
        if (!_settings.Enabled)
            return new RuntimesInfo();

        _logger.LogDebug("Detecting installed runtimes");

        return new RuntimesInfo
        {
            Python = Detect("python", "--version"),
            NodeJs = Detect("node", "--version"),
            Go = DetectGo(),
            Java = Detect("java", "-version", useStdErr: true),
            PowerShell5 = DetectPowerShell5(),
            PowerShell7 = Detect("pwsh", "--version"),
            DotNetRuntimes = DetectDotNetList("--list-runtimes"),
            DotNetSdks = DetectDotNetList("--list-sdks"),
        };
    }

    private RuntimeEntry Detect(string command, string args, bool useStdErr = false)
    {
        try
        {
            var psi = new ProcessStartInfo(command, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null)
                return new RuntimeEntry { Detected = false, Error = "Process did not start" };

            // Read both streams concurrently to avoid deadlocks if either buffer fills
            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

            if (!process.WaitForExit(_settings.TimeoutSeconds * 1000))
            {
                try { process.Kill(); } catch { /* best-effort */ }
                return new RuntimeEntry { Detected = false, Error = "Timed out" };
            }

            Task.WaitAll(stdoutTask, stderrTask);

            var stdout = stdoutTask.Result.Trim();
            var stderr = stderrTask.Result.Trim();
            var output = useStdErr ? stderr : stdout;

            // Some tools (e.g. java) write to stderr by default — fall back to the other stream
            if (string.IsNullOrWhiteSpace(output))
                output = useStdErr ? stdout : stderr;

            return new RuntimeEntry
            {
                Detected = true,
                Version = ExtractVersion(output),
                Path = ResolveCommandPath(command),
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new RuntimeEntry { Detected = false };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error detecting runtime '{Command}'", command);
            return new RuntimeEntry { Detected = false, Error = ex.Message };
        }
    }

    private RuntimeEntry DetectGo()
    {
        var entry = Detect("go", "version");
        if (entry.Detected && entry.Version != null)
        {
            // "go version go1.21.0 windows/amd64" → "1.21.0"
            var match = GoVersionRegex().Match(entry.Version);
            if (match.Success)
                entry.Version = match.Groups[1].Value;
        }
        return entry;
    }

    private RuntimeEntry DetectPowerShell5()
    {
        var entry = Detect("powershell",
            "-NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"");

        if (!entry.Detected)
        {
            var ps5Path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");

            if (File.Exists(ps5Path))
                return new RuntimeEntry { Detected = true, Version = "5.x (registry fallback)", Path = ps5Path };
        }

        return entry;
    }

    private List<DotNetRuntimeEntry> DetectDotNetList(string args)
    {
        var results = new List<DotNetRuntimeEntry>();
        try
        {
            var psi = new ProcessStartInfo("dotnet", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return results;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(_settings.TimeoutSeconds * 1000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Format: "Name Version [Path]" e.g. "Microsoft.NETCore.App 8.0.0 [C:\Program Files\dotnet\shared\...]"
                var parts = trimmed.Split(' ', 3);
                if (parts.Length >= 2)
                {
                    results.Add(new DotNetRuntimeEntry
                    {
                        Name = parts[0],
                        Version = parts[1],
                        Path = parts.Length > 2 ? parts[2].Trim('[', ']') : string.Empty,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate dotnet {Args}", args);
        }
        return results;
    }

    private static string ExtractVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return output;
        var match = VersionRegex().Match(output);
        return match.Success ? match.Value : output.Split('\n')[0].Trim();
    }

    private static string ResolveCommandPath(string command)
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                foreach (var ext in new[] { ".exe", ".cmd", ".bat", string.Empty })
                {
                    var full = Path.Combine(dir, command + ext);
                    if (File.Exists(full)) return full;
                }
            }
        }
        catch { /* best-effort */ }
        return string.Empty;
    }

    [GeneratedRegex(@"\d+\.\d+[\.\d]*")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"go(\d+\.\d+[\.\d]*)")]
    private static partial Regex GoVersionRegex();
}
