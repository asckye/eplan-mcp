using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;

namespace EplanMcp.Host.Tools;

internal static class DevicePluginDefaults
{
    private const string SymbolKey = "defaults.strip_definition.symbol";
    private const string VariantKey = "defaults.strip_definition.variant";

    internal static StripDefinitionSymbol? Select(PluginResolution resolution)
    {
        JsonElement? symbol = PluginConsumerDefaults.Value(resolution, SymbolKey);
        JsonElement? variant = PluginConsumerDefaults.Value(resolution, VariantKey);
        string[] plugins = PluginConsumerDefaults.Plugins(resolution, SymbolKey, VariantKey);
        if (symbol is null || variant is null)
        { return null; }
        if (symbol is not { ValueKind: JsonValueKind.String } text || variant is not { ValueKind: JsonValueKind.Number } number
            || !number.TryGetInt32(out int value) || value < 0)
        { throw new BridgeCallException(ToolResults.InvalidArgumentError(["Plugin strip defaults require a SPECIAL symbol name and a nonnegative integer variant."])); }
        return new() { Symbol = text.GetString()!, Variant = value, Plugins = plugins };
    }
}
