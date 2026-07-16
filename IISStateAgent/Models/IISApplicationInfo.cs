namespace IISStateAgent.Models;

public class IISApplicationInfo
{
    public string Path { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public AuthenticationInfo Authentication { get; set; } = new();
    public WebConfigInfo WebConfig { get; set; } = new();
}
