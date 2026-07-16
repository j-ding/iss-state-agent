namespace IISStateAgent.Models;

public class SslInfo
{
    public bool RequireSsl { get; set; }
    public string ClientCertificatePolicy { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
}
