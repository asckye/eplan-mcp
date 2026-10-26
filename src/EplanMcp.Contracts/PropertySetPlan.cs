namespace EplanMcp.Contracts;

/// <summary>Internal planning evidence; the Host must shape raw text before any tool or approval output.</summary>
public sealed class PropertySetPlan
{
    /// <summary>The existing pipeline plan, including only assignments that change a value.</summary>
    public WritePlan Plan { get; set; } = new();

    /// <summary>Every targeted and related function, including unchanged translations and auxiliary functions.</summary>
    public List<PropertySetGuard> Guards { get; set; } = [];

    /// <summary>Planning never starts execution, including empty plans.</summary>
    public string Execution { get; } = ExecutionState.NotStarted;
}
