namespace IISStateAgent.Models;

public class BindingInfo
{
    public string Protocol { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Hostname { get; set; } = string.Empty;
}
