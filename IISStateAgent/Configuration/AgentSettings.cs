namespace IISStateAgent.Configuration;

public class AgentSettings
{
    public string AuthenticationMode { get; set; } = "Windows";
    public int CacheDurationSeconds { get; set; } = 60;
    public RuntimeDetectionSettings RuntimeDetection { get; set; } = new();
    public List<string> MonitoredServices { get; set; } = [];
    public string ScheduledTaskFolder { get; set; } = "\\";
}

public class RuntimeDetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 5;
}
