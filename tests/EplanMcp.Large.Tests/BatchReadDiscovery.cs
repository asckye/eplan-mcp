using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Large.Tests;

/// <summary>Resolves recipes through existing read operations on the explicitly selected batch project.</summary>
internal sealed class BatchReadDiscovery(IBridgeClient bridge, string project, Func<string, Dictionary<string, object?>, CancellationToken, Task<JsonElement?>>? trial = null)
{
    private readonly PropertySchemaPaging _paging = new(TimeProvider.System);

    private readonly Dictionary<string, JsonElement?> _handles = new(StringComparer.Ordinal);

    /// <summary>
    /// A session handle of <paramref name="type"/> from one read on a producer, whose receiver is a discovery locator or
    /// a handle found the same way (up to <see cref="BatchHandles.Depth"/> steps). Kept for the run, misses included, so a
    /// chain is read once and not once per member of the type.
    /// </summary>
    private async Task<JsonElement?> HandleAsync(string type, CancellationToken cancellationToken, int depth = BatchHandles.Depth)
    {
        if (trial is null || depth <= 0)
        { return null; }
        if (_handles.TryGetValue(type, out JsonElement? known))
        { return known; }
        _handles[type] = null; // a cycle through this type ends here
        foreach (string producer in BatchHandles.Producers(type, depth))
        {
            JsonElement? receiver = BatchHandles.ReceiverKinds(producer) is { Length: > 0 } kinds
                ? await FindAsync(kinds, null, cancellationToken)
                : BatchHandles.Owner(producer) is { } owner ? await HandleAsync(owner, cancellationToken, depth - 1) : null;
            if (receiver is null)
            { continue; }
            JsonElement? value = await trial(producer, new Dictionary<string, object?> { ["target"] = receiver }, cancellationToken);
            bool viaBase = !ApiTryCatalog.Producers(type).Contains(producer, StringComparer.Ordinal);
            if (value is { } result && BatchHandles.Handle(result, type, exact: viaBase) is { } handle)
            {
                _handles[type] = handle;
                return handle;
            }
        }
        return null;
    }

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
        // An intermediate object has no discovery locator; one read on a producer of its type supplies a handle.
        JsonElement? target = entry.ObjectKinds is { Length: 0 } && BatchValues.HandleReceiver(entry.Group) is { } type
            ? await HandleAsync(type, cancellationToken)
            : entry.ObjectKinds is ["Page"]
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
