namespace EplanMcp.Contracts;

/// <summary>Read-only arguments of <see cref="OperationNames.PlanFunctions"/>; names and pages form a union.</summary>
public sealed class PlanFunctionsArgs
{
    /// <summary>The explicitly named open project, by name or path.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>1–200 exact, full function/device names; includes every main and auxiliary function with that name.</summary>
    public List<string> Names { get; set; } = [];

    /// <summary>Optionally 1–50 exact page names whose functions are also included, even unnamed functions.</summary>
    public List<string>? Pages { get; set; }
}
