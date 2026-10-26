namespace EplanMcp.Contracts;

/// <summary>Internal execution result; raw plan text must be shaped before tool output.</summary>
public sealed class PropertySetData
{
    /// <summary>The freshly checked plan.</summary>
    public WritePlan Plan { get; set; } = new();

    /// <summary>Committed, or not_started for an empty plan.</summary>
    public string Execution { get; set; } = ExecutionState.NotStarted;

    /// <summary>One undo description for the entire committed call.</summary>
    public string? Undo { get; set; }

    /// <summary>Post-commit transaction cleanup warning, if any.</summary>
    public string? Warning { get; set; }
}
