namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.ConnectionRouteExecute"/>.</summary>
public sealed class ConnectionRouteData
{
    /// <summary>The project written.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The installation space routed.</summary>
    public string Space { get; set; } = string.Empty;

    /// <summary>How many 3D connections were routed.</summary>
    public int Connections { get; set; }

    /// <summary>How many of them read back as routed.</summary>
    public int Routed { get; set; }

    /// <summary>The routed connections and the ones that could not be routed, longest first.</summary>
    public List<RoutedConnection> Items { get; set; } = [];

    /// <summary>A readback deviation that did not roll the write back (ADR-0046); null when there was none.</summary>
    public string? Warning { get; set; }
}

/// <summary>One 3D connection by its two ends, project data.</summary>
public sealed class RoutedConnection
{
    /// <summary>The device at one end, e.g. <c>=GAA+A1-FC1</c> or a terminal <c>+A1-XD1:1</c>.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Its pin, or <c>#</c> and the connection point index where the end is a connection point position (api-notes A141).</summary>
    public string FromPin { get; set; } = string.Empty;

    /// <summary>The device at the other end.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Its pin, written the same way.</summary>
    public string ToPin { get; set; } = string.Empty;

    /// <summary>Whether the connection has a route.</summary>
    public bool Routed { get; set; }

    /// <summary>The length of its route in millimetres, from the route points; 0 when it has none.</summary>
    public double LengthMm { get; set; }
}
