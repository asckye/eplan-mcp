namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One report page as an adapter reads it.</summary>
public sealed class ReportPageRecord
{
    /// <summary>The page's full name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The page's session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3).</summary>
    public string Id { get; set; } = string.Empty;
}
