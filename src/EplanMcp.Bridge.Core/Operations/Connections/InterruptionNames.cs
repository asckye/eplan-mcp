using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public static class InterruptionNames
{
    internal static string Validate(JToken? value, string path, bool trim = false)
    {
        string? raw = value?.Type == JTokenType.String ? (string?)value : null;
        string? name = trim ? raw?.Trim() : raw;
        if (name is not { Length: >= 1 and <= 200 } || string.IsNullOrWhiteSpace(name) || raw!.Any(char.IsControl))
        {
            throw WriteArguments.Invalid([path + ": Supply the shared name, 1..200 characters: 24V or -24V takes this page's higher-level function, =F-24V stays as written."]);
        }
        return name;
    }

    // A121: a short interruption name uses only the higher-level function of a structured page.
    public static string Expand(string page, string name)
    {
        string function = page.Contains('/') ? "=" + DrawingNames.HigherLevelFunctionOf(page) : string.Empty;
        return name[0] is '=' or '+' ? name : function + (name[0] == '-' ? "" : "-") + name;
    }

    public static string NativeName(string page, string expanded)
    {
        if (DrawingNames.IsBarePage(page))
        { return expanded.Length > 1 && expanded[0] == '-' ? expanded.Substring(1) : expanded; }
        return expanded.StartsWith("=", StringComparison.Ordinal) ? expanded : "=" + expanded;
    }
}
