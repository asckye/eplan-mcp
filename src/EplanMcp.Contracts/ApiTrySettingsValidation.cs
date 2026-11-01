using System.Globalization;
namespace EplanMcp.Contracts;

/// <summary>Shared scalar restrictions for the closed settings read adapters.</summary>
public static class ApiTrySettingsValidation
{
    /// <summary>Checks scope, path syntax and bounded indexes before any native settings access.</summary>
    public static bool Valid(string group, Func<string, string?> value)
    {
        if (!ApiTryCatalog.Settings(group))
        { return true; }
        string? node = value("node");
        if (node is not null && (!Path(node) || node.Split('.')[0] is not ("USER" or "STATION" or "PROJECT")
            || group.IndexOf(".ProjectSetting", StringComparison.Ordinal) >= 0 && node.Split('.')[0] != "PROJECT"
            || group.IndexOf(".Base.Settings.", StringComparison.Ordinal) >= 0 && node.Split('.')[0] == "PROJECT"))
        { return false; }
        foreach (string name in new[] { "strSettingPath", "strName" })
        { if (value(name) is { } path && !Path(path)) { return false; } }
        foreach (string name in new[] { "idx", "nIdx", "offset", "limit" })
        { if (value(name) is { } text && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < (name == "limit" ? 1 : 0) || n > (name == "limit" ? 200 : 1000000))) { return false; } }
        return true;
    }
    private static bool Path(string path) => path.Length is > 0 and <= 512 && path.Split('.').Length <= 16 && !path.Split('.').Any(string.IsNullOrWhiteSpace) && !path.Any(char.IsControl);
    /// <summary>Verifies every segment against a fresh read-only child enumeration; roots are fixed.</summary>
    public static bool Enumerated(string node, Func<string, IEnumerable<string>> children)
    {
        string[] parts = node.Split('.');
        if (!Path(node) || parts[0] is not ("USER" or "STATION" or "PROJECT"))
        { return false; }
        for (int i = 1; i < parts.Length; i++)
        { if (!children(string.Join(".", parts.Take(i))).Contains(parts[i], StringComparer.Ordinal)) { return false; } }
        return true;
    }
}
