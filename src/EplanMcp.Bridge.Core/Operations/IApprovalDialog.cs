using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The approval dialog in EPLAN (M3 design 4.3, 4.7), implemented by the EPLAN adapter layer: modal to EPLAN's main
/// window, on the main thread, inside the dispatcher's call, so other calls get EPLAN_BUSY while it is open. Its timing
/// rules are <see cref="ApprovalCountdown"/>'s.
/// </summary>
public interface IApprovalDialog
{
    /// <summary>
    /// Shows <paramref name="question"/> until the engineer decides or <paramref name="timeout"/> has passed since it
    /// appeared, and returns one of <see cref="ApprovalDecisions"/>.
    /// </summary>
    public string Ask(ApprovalAskArgs question, TimeSpan timeout);
}
