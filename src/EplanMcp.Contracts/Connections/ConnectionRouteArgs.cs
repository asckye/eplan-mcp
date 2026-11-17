namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ConnectionRoutePlan"/> and <see cref="OperationNames.ConnectionRouteExecute"/>.</summary>
public sealed class ConnectionRouteArgs
{
    /// <summary>Project name or path; required for a write.</summary>
    public string? Project { get; set; }

    /// <summary>Installation space designation, e.g. <c>A1</c> (a leading '+' is dropped).</summary>
    public string? Space { get; set; }

    /// <summary><c>network</c> (along ducts and routing segments, the default) or <c>direct</c>.</summary>
    public string? Routing { get; set; }

    /// <summary>Route only the connections that end at one of these devices; empty for every connection of the space.</summary>
    public List<string> Devices { get; set; } = [];

    /// <summary>For the execution: the fingerprint the plan returned.</summary>
    public string? Fingerprint { get; set; }
}
