namespace EplanMcp.Contracts;

/// <summary>Closed lifecycle naming policy shared by planning and approval presentation.</summary>
public static class ApiTryLifecycle
{
    private static readonly string[] _removePrefixes = ["Remove", "Delete", "Erase"];
    /// <summary>The explicit effect shown for lifecycle calls, or null for other groups.</summary>
    public static string? Effect(string group)
    {
        string name = group.Substring(group.LastIndexOf('.') + 1);
        return name.StartsWith("Create", StringComparison.Ordinal) || name == "#ctor+Create" ? "creates"
            : _removePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) ? "removes" : null;
    }
}
