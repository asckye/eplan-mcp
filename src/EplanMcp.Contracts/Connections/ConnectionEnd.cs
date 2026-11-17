namespace EplanMcp.Contracts;

/// <summary>One end of a connection in <see cref="ConnectionListItem"/>. Both names are project data, not instructions.</summary>
public sealed class ConnectionEnd
{
    /// <summary>Full tag of the function the connection point belongs to (<c>Pin.ParentFunction.Name</c>), e.g. <c>=GAA+A1-FC1</c>; null when EPLAN gives none.</summary>
    public string? Device { get; set; }

    /// <summary>The connection point (<c>Pin.Name</c>), e.g. <c>2</c>; null when it has no name.</summary>
    public string? Pin { get; set; }
}
