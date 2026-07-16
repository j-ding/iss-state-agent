namespace IISStateAgent.Models;

public class RuntimesInfo
{
    public RuntimeEntry Python { get; set; } = new();
    public RuntimeEntry NodeJs { get; set; } = new();
    public RuntimeEntry Go { get; set; } = new();
    public RuntimeEntry Java { get; set; } = new();
    public RuntimeEntry PowerShell5 { get; set; } = new();
    public RuntimeEntry PowerShell7 { get; set; } = new();
    public List<DotNetRuntimeEntry> DotNetRuntimes { get; set; } = [];
    public List<DotNetRuntimeEntry> DotNetSdks { get; set; } = [];
}

public class RuntimeEntry
{
    public bool Detected { get; set; }
    public string? Version { get; set; }
    public string? Path { get; set; }
    public string? Error { get; set; }
}

public class DotNetRuntimeEntry
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
