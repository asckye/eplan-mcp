using System.Runtime.Versioning;
using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

internal sealed record BatchArguments(Dictionary<string, object?>? Values, string? Outcome = null, string? Detail = null);

/// <summary>Read-only argument recipes; values are detached discovery results, never native objects.</summary>
[SupportedOSPlatform("windows")]
internal sealed class BatchReadRecipes
{
    private readonly Dictionary<string, string> _sources;
    internal static BatchReadRecipes Default { get; } = Load();

    private BatchReadRecipes(Dictionary<string, string> sources) => _sources = sources;

    internal static BatchReadRecipes Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            string? source = property.Value.GetString();
            if (source is not ("locator" or "project" or "circuit-page" or "enum") || !sources.TryAdd(property.Name, source))
            { throw new FormatException("Unknown or duplicate read recipe: " + property.Name); }
        }
        return new(sources);
    }

    private static BatchReadRecipes Load()
    {
        using var reader = new StreamReader(typeof(BatchReadRecipes).Assembly.GetManifestResourceStream("BatchReadRecipes.json")!);
        return Parse(reader.ReadToEnd());
    }

    internal string[] SelectMethod(string group)
    {
        return ApiTryCatalog.Methods(group).OrderBy(m => !CanBuild(new(group, "api-read", BatchValues.ReceiverKinds(m[1]),
            Parameters: m.Skip(2).ToDictionary(n => n, n => ApiTryCatalog.ParameterShape(m[1], n)!, StringComparer.Ordinal), Method: true, Signature: m[1])))
            .ThenBy(m => m.Length).ThenBy(m => m[1], StringComparer.Ordinal).First();
    }

    // An empty receiver is buildable when a producer of that type supplies a session handle (BatchHandles).
    internal bool CanBuild(BatchEntry entry) => entry.Kind == "api-read"
        && (entry.ObjectKinds is not { Length: 0 } || BatchValues.HandleReceiver(entry.Group) is not null)
        && (entry.Parameters?.All(p => Source(p.Value) is not null) ?? true) && ValidPreconditions(entry, _ => true);

    private string? Source(string[] shape)
    {
        string type = shape[0] == "array" ? shape[1] : shape[0];
        return _sources.GetValueOrDefault(type);
    }

    internal async Task<BatchArguments> ArgumentsAsync(BatchEntry entry, Func<string, string[], Task<JsonElement?>> find)
    {
        if (!CanBuild(entry))
        { return new(null, "failed:needs_context", "No read recipe or explicit page overload for this call."); }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        // Resolve the page first so a page-bound object can be discovered on that same page (api-notes/actions A63).
        foreach ((string name, string[] shape) in (entry.Parameters ?? []).OrderBy(p => Source(p.Value) != "circuit-page"))
        {
            bool array = shape[0] == "array";
            string[] element = array ? shape[1..] : shape;
            string source = Source(shape)!;
            string[] kinds = [.. ObjectReadTypes.Types.Where(ObjectReadTypes.Supported)
                .Where(k => ApiTryCatalog.Kinds(element[0]).Contains(k, StringComparer.OrdinalIgnoreCase))];
            object? value = source == "enum" ? element.ElementAtOrDefault(1) : await find(source, kinds);
            if (value is null)
            { return new(null, "skipped:no_context_value", "Read recipe returned no value for " + name + " (" + element[0] + ")."); }
            values[name] = array ? new[] { value } : value;
        }
        if (!ValidPreconditions(entry, name => values.GetValueOrDefault(name) is JsonElement locator
            && locator.TryGetProperty("page", out JsonElement page) && !string.IsNullOrWhiteSpace(page.GetString())))
        { return new(null, "skipped:no_context_value", "Read recipe requires a placed object on a valid page."); }
        return new(values);
    }

    private static bool ValidPreconditions(BatchEntry entry, Func<string, bool> hasPage)
    {
        if (entry.Signature is null)
        { return true; }
        try
        { ApiTryPreconditions.Check(entry.Group, ["", entry.Signature, .. entry.Parameters!.Keys], hasPage); return true; }
        catch (BridgeErrorException)
        { return false; }
    }
}
