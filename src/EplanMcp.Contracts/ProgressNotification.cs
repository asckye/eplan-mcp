namespace EplanMcp.Contracts;

/// <summary>
/// Parameters of the <see cref="BridgeProtocol.ProgressMethod"/> notification (M4 design 4.3). The Bridge keeps only
/// the newest report of an invoke and sends at a limited rate, so the Host may not see every step.
/// </summary>
public sealed class ProgressNotification
{
    /// <summary>The request ID (<c>correlationId</c>) of the invoke this reports on.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Steps done so far, such as pages exported; never decreases within one invoke.</summary>
    public int Progress { get; set; }

    /// <summary>The number of steps when it is known; null otherwise.</summary>
    public int? Total { get; set; }

    /// <summary>
    /// A short English phrase about the step, such as "Exporting page 3 of 20"; null when there is none. Never project
    /// content such as page names or texts: progress reaches the client without the shaping that results get
    /// (architecture 8.5).
    /// </summary>
    public string? Message { get; set; }
}
