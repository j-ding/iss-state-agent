using System.Xml.Linq;
using IISStateAgent.Models;

namespace IISStateAgent.Services;

public class WebConfigReader
{
    private readonly ILogger<WebConfigReader> _logger;

    public WebConfigReader(ILogger<WebConfigReader> logger)
    {
        _logger = logger;
    }

    public WebConfigInfo Read(string physicalPath)
    {
        if (string.IsNullOrWhiteSpace(physicalPath))
            return new WebConfigInfo();

        var webConfigPath = Path.Combine(physicalPath, "web.config");

        if (!File.Exists(webConfigPath))
            return new WebConfigInfo();

        try
        {
            var doc = XDocument.Load(webConfigPath);
            var root = doc.Root;

            if (root == null)
                return new WebConfigInfo { Found = true };

            return new WebConfigInfo
            {
                Found = true,
                AppSettingKeys = ReadAppSettingKeys(root),
                ConnectionStringNames = ReadConnectionStringNames(root),
                CustomErrorsMode = ReadAttribute(root, "system.web/customErrors", "mode"),
                SessionStateMode = ReadAttribute(root, "system.web/sessionState", "mode"),
                TrustLevel = ReadAttribute(root, "system.web/trust", "level"),
                CustomResponseHeaders = ReadCustomResponseHeaders(root),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read web.config at {Path}", webConfigPath);
            return new WebConfigInfo { Found = true };
        }
    }

    private static List<string> ReadAppSettingKeys(XElement root)
    {
        return root.Descendants("appSettings")
            .FirstOrDefault()
            ?.Elements("add")
            .Select(e => e.Attribute("key")?.Value ?? string.Empty)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList() ?? [];
    }

    private static List<string> ReadConnectionStringNames(XElement root)
    {
        return root.Descendants("connectionStrings")
            .FirstOrDefault()
            ?.Elements("add")
            .Select(e => e.Attribute("name")?.Value ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList() ?? [];
    }

    private static string? ReadAttribute(XElement root, string xpath, string attribute)
    {
        var parts = xpath.Split('/');
        XElement? current = root;

        foreach (var part in parts)
        {
            current = current?.Descendants(part).FirstOrDefault();
            if (current == null) return null;
        }

        return current?.Attribute(attribute)?.Value;
    }

    private static List<string> ReadCustomResponseHeaders(XElement root)
    {
        return root.Descendants("customHeaders")
            .FirstOrDefault()
            ?.Elements("add")
            .Select(e => e.Attribute("name")?.Value ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList() ?? [];
    }
}
