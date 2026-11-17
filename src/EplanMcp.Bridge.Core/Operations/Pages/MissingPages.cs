using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The PAGE_NOT_FOUND refusal of the page-scoped write requests. Project and page names go to metadata only, never into
/// the message (ADR-0053, WS-28).
/// </summary>
internal static class MissingPages
{
    public static BridgeErrorException Error(string project, IReadOnlyCollection<string> missing, string nothingDone) => new(
        ErrorReason.PageNotFound,
        $"The project has no page with exactly these names (letter case counts); metadata.missing lists them. {nothingDone} Use eplan_page_list to find the exact names.",
        retryable: false,
        new Dictionary<string, string> { ["project"] = project, ["missing"] = string.Join("\n", missing) });
}
