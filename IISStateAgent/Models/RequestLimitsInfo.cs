namespace IISStateAgent.Models;

public class RequestLimitsInfo
{
    public long MaxContentLengthBytes { get; set; }
    public int MaxUrlLength { get; set; }
    public int MaxQueryStringLength { get; set; }
}
