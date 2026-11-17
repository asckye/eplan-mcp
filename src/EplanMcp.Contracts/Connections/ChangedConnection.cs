namespace EplanMcp.Contracts;

/// <summary>A connection that generating connections added or removed, by its ends as <see cref="ConnectionListItem"/> gives them.</summary>
public sealed class ChangedConnection
{
    /// <summary>Where the connection starts; null when it is not connected there.</summary>
    public ConnectionEnd? From { get; set; }

    /// <summary>Where the connection ends; null when it is not connected there.</summary>
    public ConnectionEnd? To { get; set; }
}
