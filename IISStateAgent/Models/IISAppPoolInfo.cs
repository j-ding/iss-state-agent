namespace IISStateAgent.Models;

public class IISAppPoolInfo
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string PipelineMode { get; set; } = string.Empty;
    public string StartMode { get; set; } = string.Empty;
    public int IdleTimeoutMinutes { get; set; }
    public bool AutoStart { get; set; }
    public int MaxWorkerProcesses { get; set; }
}
