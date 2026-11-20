namespace EplanMcp.Contracts;

/// <summary>Closed lifecycle naming policy shared by planning and approval presentation.</summary>
public static class ApiTryLifecycle
{
    private static readonly string[] _removePrefixes = ["Remove", "Delete", "Erase"];
    /// <summary>
    /// The explicit effect shown for lifecycle calls, or null for other groups. Parts database objects have none
    /// (ADR-0059 tier 2): they are not project objects, their removal has no undo step and no project observation, and
    /// the master data target of the write already asks for approval and a backup (ADR-0019, ADR-0054).
    /// </summary>
    public static string? Effect(string group)
    {
        if (group.StartsWith("O:Eplan.EplApi.MasterData.", StringComparison.Ordinal))
        { return null; }
        string name = group.Substring(group.LastIndexOf('.') + 1);
        return name.StartsWith("Create", StringComparison.Ordinal) ? "creates"
            : _removePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) ? "removes" : null;
    }
}
