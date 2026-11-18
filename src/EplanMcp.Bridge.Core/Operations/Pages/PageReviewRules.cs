using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Detached, read-only page rules; electrical crossings require target-network evidence (A52/A64/A65). Each check is a
/// class of its own in Pages/Review and adds its issues to the shared <see cref="ReviewContext"/>.
/// </summary>
public static class PageReviewRules
{
    // The order is part of the result: issues are returned, and paged by the Host, in the order the checks add them.
    private static readonly Action<ReviewContext>[] _checks =
    [
        WireBendGridCheck.Run,
        DanglingEndsCheck.Run,
        UnconnectedPinsCheck.Run,
        PlacementGridAndOverlapCheck.Run,
        OffAreaCheck.Run,
        LayoutRuleChecks.Run,
        PathStraddleCheck.Run,
        PageSetupCheck.Run,
        WireNumberCheck.Run,
        PlaceholderTagCheck.Run,
        BoxCrossingsCheck.Run,
        UnintendedJunctionsCheck.Run,
        InterruptionPairingCheck.Run,
        CableCoresCheck.Run,
        EplanMessagesCheck.Run,
    ];

    internal static List<LayoutGuard.Violation> GuardViolations(PageScene scene) => ReviewGuard.Violations(scene);

    public static JArray Check(PageScene scene, JObject actual, JObject request)
    {
        var context = new ReviewContext(scene, actual, request);
        foreach (Action<ReviewContext> check in _checks)
        {
            check(context);
        }
        return context.Issues;
    }
}
