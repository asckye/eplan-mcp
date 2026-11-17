using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Packs;

namespace EplanMcp.Host.Tools;

internal static class ConnectionPluginDefaults
{
    private const string SymbolKey = "defaults.interruption.symbol";

    internal static (JsonElement[] Items, string[] Plugins) Apply(JsonElement[] items, PluginResolution resolution)
    {
        bool NeedsSymbol(JsonElement item) => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("kind", out JsonElement kind) && kind.ValueKind == JsonValueKind.String
            && kind.GetString() == "interruption_point" && !item.TryGetProperty("symbol", out _);
        if (!items.Any(NeedsSymbol) || PluginConsumerDefaults.Value(resolution, SymbolKey) is not { } symbol)
        { return (items, []); }
        if (symbol.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(symbol.GetString()))
        { throw new Bridge.BridgeCallException(ToolResults.InvalidArgumentError(["/plugins/defaults.interruption.symbol: configure one SPECIAL symbol name."])); }
        return ([.. items.Select(item =>
        {
            if (!NeedsSymbol(item))
            { return item; }
            JsonObject copy = JsonNode.Parse(item.GetRawText())!.AsObject();
            copy["symbol"] = symbol.GetString();
            return JsonSerializer.SerializeToElement(copy);
        })], PluginConsumerDefaults.Plugins(resolution, SymbolKey));
    }
}
