namespace IISStateAgent.Models;

public class AuthenticationInfo
{
    public bool AnonymousEnabled { get; set; }
    public bool WindowsEnabled { get; set; }
    public bool BasicEnabled { get; set; }
    public bool DigestEnabled { get; set; }
}
