namespace IISStateAgent.Models;

public class ServerSnapshot
{
    public string Hostname { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string AgentVersion { get; set; } = string.Empty;
    public List<IISSiteInfo> Sites { get; set; } = [];
    public List<IISAppPoolInfo> AppPools { get; set; } = [];
    public RuntimesInfo Runtimes { get; set; } = new();
    public WindowsEnvironmentInfo Environment { get; set; } = new();
    public List<string> Errors { get; set; } = [];
}
