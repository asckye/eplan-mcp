namespace EplanMcp.Contracts;

/// <summary>The complete union requested by <see cref="PlanFunctionsArgs"/>, or an explicitly bounded prefix.</summary>
public sealed class PlanFunctionsData
{
    /// <summary>The resolved project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The resolved project path without extension.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Functions sorted ordinally by name, page (null first), type, then oid.</summary>
    public List<PlanFunction> Functions { get; set; } = [];

    /// <summary>False means the list must not be used to prove a plan's impact closure.</summary>
    public bool Complete { get; set; }

    /// <summary>The maximum number of functions returned, currently 10,000.</summary>
    public int Limit { get; set; }
}
