using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Server;

/// <summary>Every page of a tool's result, or why they could not all be read.</summary>
internal sealed record PagedOutcome(IReadOnlyList<JsonObject> Pages, string? Problem);

/// <summary>
/// Calls a tool and follows <c>nextCursor</c> to the last page, so an expected value is never cut by the page size or
/// the size budget of a list tool (the Host sets <c>truncated</c> and points <c>nextCursor</c> at the rest).
/// </summary>
internal static class ToolPages
{
    public const int MaxPages = 100;

    public static async Task<PagedOutcome> AllAsync(IToolClient client, string tool, JsonObject args, CancellationToken cancellationToken)
    {
        var pages = new List<JsonObject>();
        string? cursor = null;
        while (true)
        {
            var call = (JsonObject)args.DeepClone();
            if (cursor is not null)
            {
                call["cursor"] = cursor;
            }

            ToolOutcome outcome = await client.CallAsync(tool, call, cancellationToken).ConfigureAwait(false);
            if (outcome.IsError)
            {
                return new PagedOutcome(pages, $"{tool} failed: {outcome.Text}");
            }

            if (outcome.Structured is not { } page)
            {
                return new PagedOutcome(pages, $"{tool} returned no structured result");
            }

            pages.Add(page);
            string? next = Comparison.Text(page["nextCursor"]);
            if (next is null)
            {
                return new PagedOutcome(pages, null);
            }

            // The same cursor again means not even the next entry fits the size budget.
            if (next == cursor)
            {
                return new PagedOutcome(pages, $"{tool} cannot return its next entry within the size budget; ask for less detail");
            }

            if (pages.Count == MaxPages)
            {
                return new PagedOutcome(pages, $"{tool} has more than {MaxPages.ToString(CultureInfo.InvariantCulture)} pages; narrow the arguments");
            }

            cursor = next;
        }
    }
}
