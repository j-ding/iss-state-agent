namespace IISStateAgent.Models;

public class WindowsEnvironmentInfo
{
    public List<IISModuleInfo> IISModules { get; set; } = [];
    public List<WindowsServiceInfo> MonitoredServices { get; set; } = [];
    public List<ScheduledTaskInfo> ScheduledTasks { get; set; } = [];
    public List<HostsEntry> HostsFileEntries { get; set; } = [];
}

public class IISModuleInfo
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}

public class WindowsServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartType { get; set; } = string.Empty;
}

public class ScheduledTaskInfo
{
    public string TaskName { get; set; } = string.Empty;
    public string TaskPath { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? LastRunTime { get; set; }
    public string? NextRunTime { get; set; }
    public string LastTaskResult { get; set; } = string.Empty;
}

public class HostsEntry
{
    public string IPAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
}
