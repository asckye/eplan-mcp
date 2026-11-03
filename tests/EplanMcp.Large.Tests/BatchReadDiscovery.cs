using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Large.Tests;

/// <summary>Resolves recipes through existing read operations on the explicitly selected batch project.</summary>
internal sealed class BatchReadDiscovery(IBridgeClient bridge, string project)
{
    private readonly PropertySchemaPaging _paging = new(TimeProvider.System);

    internal async Task<BatchArguments> ArgumentsAsync(BatchEntry entry, CancellationToken cancellationToken)
    {
        string? circuitPage = null;
        async Task<JsonElement?> ResolveAsync(string source, string[] kinds)
        {
            if (source == "circuit-page")
            {
                // The list operation supplies a stable first Circuit page (A11/A12).
                ListData<PageSummary> pages = await bridge.InvokeAsync<PageListArgs, ListData<PageSummary>>(OperationNames.PageList,
                    new() { Project = project, PageType = "Circuit", Limit = 1 }, cancellationToken);
                circuitPage = pages.Items.FirstOrDefault()?.Name;
                return circuitPage is null ? null : await FindAsync(["Page"], circuitPage, cancellationToken);
            }
            return await FindAsync(source == "project" ? ["Project"] : kinds, circuitPage, cancellationToken);
        }

        BatchArguments arguments = await BatchReadRecipes.Default.ArgumentsAsync(entry, ResolveAsync);
        if (arguments.Values is null || entry.ObjectKinds is null)
        { return arguments; }
        JsonElement? target = entry.ObjectKinds is ["Page"]
            ? await ResolveAsync("circuit-page", ["Page"])
            : await FindAsync(entry.ObjectKinds, null, cancellationToken);
        if (target is null)
        { return new(null, "skipped:no_object", "No receiver was found in the batch project."); }
        arguments.Values["target"] = target;
        return arguments;
    }

    private async Task<JsonElement?> FindAsync(string[] kinds, string? page, CancellationToken cancellationToken)
    {
        foreach (string kind in kinds)
        {
            // Project and library discovery must not inherit a page filter from another parameter.
            var input = new Dictionary<string, object?> { ["project"] = project, ["object_type"] = kind, ["limit"] = 1 };
            if (page is not null && kind is not ("Project" or "SymbolLibrary"))
            { input["page"] = page; }
            var call = await ObjectReadTools.ReadAsync(bridge, _paging, false, JsonSerializer.SerializeToElement(input), cancellationToken);
            if (call.IsError == true)
            {
                using JsonDocument error = JsonDocument.Parse(string.Join("\n", call.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
                throw new BridgeCallException(error.RootElement.GetProperty("error").Deserialize<BridgeError>(HostJson.Options)!);
            }
            if (call.StructuredContent!.Value.GetProperty("items").EnumerateArray()
                .Select(i => (JsonElement?)i.GetProperty("locator").Clone()).FirstOrDefault() is { } locator)
            { return locator; }
        }
        return null;
    }
}
