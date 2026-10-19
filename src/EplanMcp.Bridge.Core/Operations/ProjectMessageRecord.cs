namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One project message (<c>EServices.ProjectMessage</c>) as an adapter reads it, EPLAN's values unchanged.</summary>
public sealed class ProjectMessageRecord
{
    /// <summary><c>GetId()</c>: the message number.</summary>
    public int Id { get; set; }

    /// <summary><c>GetCategory()</c> as EPLAN names it: <c>Hint</c>, <c>Warning</c>, <c>Error</c> or <c>Unknown</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary><c>GetGroup()</c> as EPLAN names the region, for example <c>Connections</c>.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary><c>GetText()</c>: the text with its parameters filled in.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary><c>GetDone()</c>.</summary>
    public bool Done { get; set; }

    /// <summary>EPLAN's type name of <c>GetObject1()</c>; null when the message names no object.</summary>
    public string? ObjectKind { get; set; }

    /// <summary>The object's full tag or full page name; null for other kinds or without an object.</summary>
    public string? ObjectName { get; set; }
}
