namespace EplanMcp.Host.Addin;

/// <summary>
/// How long install-addin waits: for the started EPLAN to load the add-in (2 minutes, M5 design 4.2); after that,
/// before closing an EPLAN that runs <c>/unregister</c>; for it to close, asking again every <see cref="Resend"/>; and
/// between looks.
/// </summary>
/// <remarks>
/// The pause before closing is there because a registered add-in loads while EPLAN starts, before EPLAN runs
/// <c>/unregister</c> (api-notes A23). With 10 seconds the live check saw the unregistration take; how long the action
/// takes has not been measured. EPLAN ignores a close request for some seconds after the add-in loaded, while it still
/// finishes starting (A23), hence asking again.
/// </remarks>
internal sealed record AddinTimings(TimeSpan Load, TimeSpan Settle, TimeSpan Close, TimeSpan Resend, TimeSpan Poll)
{
    public static AddinTimings Default { get; } =
        new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(500));
}
