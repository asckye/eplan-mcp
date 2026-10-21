namespace EplanMcp.Contracts;

/// <summary>A connection whose wire number EPLAN changed. Values are project data.</summary>
public sealed class NumberedConnection
{
    /// <summary>The wire number before; null when it had none.</summary>
    public string? From { get; set; }

    /// <summary>The wire number after; null when it has none.</summary>
    public string? To { get; set; }

    /// <summary>The full name of the page the connection is placed on; null for a 3D connection or one not placed.</summary>
    public string? Page { get; set; }
}
