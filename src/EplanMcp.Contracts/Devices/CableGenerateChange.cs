namespace EplanMcp.Contracts;

/// <summary>A change identified by session object identity before truncation; names are untrusted project data.</summary>
public sealed class CableGenerateChange
{
    /// <summary>created, updated or removed; replacement counts as removal and creation.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The cable's full name.</summary>
    public string Name { get; set; } = string.Empty;
}
