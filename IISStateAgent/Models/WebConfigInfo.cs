namespace IISStateAgent.Models;

public class WebConfigInfo
{
    public bool Found { get; set; }
    public List<string> AppSettingKeys { get; set; } = [];
    public List<string> ConnectionStringNames { get; set; } = [];
    public string? CustomErrorsMode { get; set; }
    public string? SessionStateMode { get; set; }
    public string? TrustLevel { get; set; }
    public List<string> CustomResponseHeaders { get; set; } = [];
}
