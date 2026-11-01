using System.Text.Json;
using EplanMcp.Contracts;
namespace EplanMcp.Host.Tools;

internal sealed partial class ApiTryTools
{
    private static bool EntryParameters(string group, JsonElement p)
    {
        string[]? shape = ApiTryCatalog.Shape(group, p.EnumerateObject().Select(v => v.Name));
        if (shape is null)
        { return ApiTryCatalog.MethodAccess(group) is null; }
        bool statik = ApiTryCatalog.Static(shape[1]), via = p.TryGetProperty("via", out JsonElement path), key = p.TryGetProperty("via_key", out JsonElement selector);
        if (!statik && ApiTryCatalog.Entry(shape[1]) && !via)
        { return false; }
        bool setter = ApiTryCatalog.MethodAccess(group) is null && p.TryGetProperty("value", out _);
        if (statik ? p.TryGetProperty("target", out _) || via || key : !p.TryGetProperty("target", out JsonElement target) || Kind(target) != "locator")
        { return false; }
        if (via && (ApiTryCatalog.Service(group) || path.ValueKind != JsonValueKind.String || ApiTryCatalog.ViaKinds(shape[0], path.GetString()!).Length == 0)
            || key != (via && ApiTryCatalog.ViaKey(shape[0], path.GetString()!)) || key && (selector.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(selector.GetString())))
        { return false; }
        bool Valid(string[]? valueShape, JsonElement value) => ApiTryCatalog.ValidValue(valueShape, Kind(value), value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText());
        return p.EnumerateObject().Count() == shape.Length - 2 + (statik ? 0 : 1) + (setter ? 1 : 0) + (via ? 1 : 0) + (key ? 1 : 0)
            && p.EnumerateObject().All(v => v.Name is "target" or "via" or "via_key" || setter && v.Name == "value" && Valid(ApiTryCatalog.ValueShape(group), v.Value)
                || shape.Skip(2).Contains(v.Name, StringComparer.Ordinal) && Valid(ApiTryCatalog.ParameterShape(shape[1], v.Name), v.Value));
    }
}
