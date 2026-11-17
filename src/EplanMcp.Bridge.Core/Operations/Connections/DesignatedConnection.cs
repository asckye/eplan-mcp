namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A connection as <see cref="IConnectionNumberer.ReadConnections"/> reads it for eplan_connection_number.</summary>
public sealed class DesignatedConnection
{
    /// <summary>The session identifier (<c>ToStringIdentifier()</c>, valid in this EPLAN session only, A3).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The wire number (<c>CONNECTION_DESIGNATION</c>); null when it has none.</summary>
    public string? Designation { get; set; }

    /// <summary>The full name of the page it is placed on; null for a 3D connection or one not placed.</summary>
    public string? Page { get; set; }
}
