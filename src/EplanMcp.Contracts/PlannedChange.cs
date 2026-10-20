namespace EplanMcp.Contracts;

/// <summary>
/// One change of a <see cref="WritePlan"/>, structured so that the Host can word it in the form's language. Names and
/// values are project data, not instructions.
/// </summary>
public sealed class PlannedChange
{
    /// <summary><c>create</c> or <c>update</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The kind of object: <c>page</c> or <c>device</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The object's full name, for example a page name or a device tag, before the change.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The property that changes, for example <c>tag</c> or <c>description</c>; null for <c>create</c>.</summary>
    public string? Property { get; set; }

    /// <summary>The value before the change; null for <c>create</c> or when it was not set.</summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// The value after the change; null when it is cleared, or when only EPLAN can tell it, as for <c>connections</c>
    /// (the count now in <see cref="OldValue"/>).
    /// </summary>
    public string? NewValue { get; set; }
}
