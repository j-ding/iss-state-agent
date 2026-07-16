namespace IISStateAgent.Models;

public class IISSiteInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<BindingInfo> Bindings { get; set; } = [];
    public AuthenticationInfo Authentication { get; set; } = new();
    public SslInfo Ssl { get; set; } = new();
    public RequestLimitsInfo RequestLimits { get; set; } = new();
    public List<IISApplicationInfo> Applications { get; set; } = [];
}
