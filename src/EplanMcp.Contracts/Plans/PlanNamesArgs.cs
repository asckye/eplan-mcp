namespace EplanMcp.Contracts;

/// <summary>Read-only arguments of <see cref="OperationNames.PlanNames"/>.</summary>
public sealed class PlanNamesArgs
{
    /// <summary>The explicitly named open project, by name or path.</summary>
    public string Project { get; set; } = string.Empty;
}
