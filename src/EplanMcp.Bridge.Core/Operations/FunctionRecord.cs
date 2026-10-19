namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One EPLAN function as an adapter reads it, EPLAN's values unchanged, before <see cref="DeviceTags"/> apply. Used by
/// the page and device operations.
/// </summary>
public sealed class FunctionRecord
{
    /// <summary><c>Function.Name</c>: the full tag with structure, e.g. <c>=GAA+A1-FC1</c> (api-notes A11); empty for a function without a name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Function.VisibleName</c> as EPLAN gives it: may be empty, carry '&gt;' markers or equal <see cref="Name"/> (S6).</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary><c>Function.IsMainFunction</c>.</summary>
    public bool IsMain { get; set; }

    /// <summary>EPLAN's <c>FunctionCategory</c> name, e.g. <c>CircuitBreaker</c>.</summary>
    public string Category { get; set; } = string.Empty;
}
