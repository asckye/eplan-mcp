using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public sealed partial class ApiTryPlanOperation
{
    private static bool EntryParameters(string group, string[] shape, JObject p, bool setter, string project)
    {
        bool statik = ApiTryCatalog.Static(shape[1]), via = p.ContainsKey("via"), key = p.ContainsKey("via_key");
        if (!statik && ApiTryCatalog.Entry(shape[1]) && !via && !ApiTryCatalog.SettingsMethods(group).Any(s => s.SequenceEqual(shape)) && (p["target"] is not JObject handle || !Handle(handle)))
        { return false; }
        if (statik ? p.ContainsKey("target") || via || key : p["target"] is not JObject target || !(Locator(target) || !via && Handle(target)))
        { return false; }
        if (via && (ApiTryCatalog.Service(group) || p["via"]?.Type != JTokenType.String || ApiTryCatalog.ViaKinds(shape[0], (string)p["via"]!).Length == 0)
            || key != (via && ApiTryCatalog.ViaKey(shape[0], (string)p["via"]!)) || key && (p["via_key"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p["via_key"]!)))
        { return false; }
        return p.Count == shape.Length - 2 + (statik ? 0 : 1) + (setter ? 1 : 0) + (via ? 1 : 0) + (key ? 1 : 0)
            && (!setter || ValidValue(ApiTryCatalog.ValueShape(group), p["value"]!))
            && p.Properties().All(v => v.Name is "target" or "via" or "via_key" || setter && v.Name == "value" || shape.Skip(2).Contains(v.Name, StringComparer.Ordinal) && ValidValue(ApiTryCatalog.ParameterShape(shape[1], v.Name), v.Value))
            && !p.Properties().Any(v => Kind(v.Value) == "locator" && !ProjectResolver.SamePath(project, (string)v.Value["project"]!));
    }
}
