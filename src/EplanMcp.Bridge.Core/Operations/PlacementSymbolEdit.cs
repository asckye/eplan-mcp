using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class PlacementSymbolEdit
{
    public static JObject Plan(JObject input, JObject before, ISymbolReader? symbols, string? fingerprint)
    {
        void Require(bool valid, string message)
        { if (!valid) { throw WriteArguments.Invalid([message]); } }
        ConnectionCreatePlanner.Members(input, "", "project page operation locators symbol_variant", "dry_run response_format");
        Require(input.ToString(Formatting.None).Length <= 65536 && input["locators"] is JArray { Count: >= 1 and <= 50 }, "Supply 1..50 locators within 64 KiB.");
        Require(input["project"]?.Type == JTokenType.String && input["page"]?.Type == JTokenType.String && (string?)input["page"] == (string?)before["scene"]!["page"]!["page"], "Use the explicit open project and page.");
        Require((string?)input["project"] == (string?)before["scene"]!["project"]!["name"] || ProjectResolver.SamePath((string)input["project"]!, (string)before["scene"]!["project"]!["path"]!), "Use the explicit open project.");
        Require(input["dry_run"] is null || input["dry_run"]!.Type == JTokenType.Boolean, "dry_run must be boolean.");
        Require(input["response_format"] is null || (string?)input["response_format"] is "concise" or "detailed", "Use concise or detailed.");
        Require(input["symbol_variant"]?.Type == JTokenType.Integer && input["symbol_variant"]!.Value<double>() is >= 0 and <= int.MaxValue, "symbol_variant must be a nonnegative integer.");
        string hash = Fingerprint.Of([input.ToString(Formatting.None), before.ToString(Formatting.None)]);
        if (fingerprint is not null && fingerprint != hash)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Object evidence changed; find and preview again."); }
        var predicted = new JArray();
        string? warning = null;
        foreach (JToken locator in input["locators"]!)
        {
            ConnectionCreatePlanner.Members(locator, "/locators", "project page type oid digest", "");
            JToken root = before["objects"]!.SingleOrDefault(o => JToken.DeepEquals(o["locator"], locator)) ?? throw new BridgeErrorException(ErrorReason.PlanStale, "Refresh the exact find locator.");
            Require(!predicted.Any(p => JToken.DeepEquals(p["locator"], locator)), "Remove duplicate locators.");
            Require(root["symbol"] is JObject, "The selected object has no symbol.");
            var available = root["symbol"]!["name"] is null ? null : symbols?.Read((string)before["scene"]!["project"]!["path"]!, new SymbolSearchArgs { Library = (string?)root["symbol"]!["library"]!, NameContains = (string?)root["symbol"]!["name"]! }).Where(s => s.Library == (string?)root["symbol"]!["library"] && s.Name == (string?)root["symbol"]!["name"]).Select(s => s.Variant).Distinct().OrderBy(v => v).ToArray();
            if (available is null)
            { warning = "Available variants could not be read; EPLAN must validate the requested variant."; }
            Require(available is null || available.Contains((int)input["symbol_variant"]!), "Variant unavailable. Available variants: " + string.Join(", ", available ?? []));
            var row = (JObject)root.DeepClone();
            row["symbol"]!["variant"] = input["symbol_variant"]!.DeepClone();
            predicted.Add(row);
        }
        return new JObject { ["project"] = before["scene"]!["project"]!["name"]!.DeepClone(), ["page"] = input["page"]!.DeepClone(), ["operation"] = input["operation"]!.DeepClone(), ["fingerprint"] = hash, ["predicted"] = predicted, ["removedConnections"] = new JArray(), ["newLocators"] = new JArray(), ["noOp"] = predicted.All(p => before["objects"]!.Any(o => JToken.DeepEquals(p, o))), ["warning"] = warning + " Symbol connection directions may change; call eplan_connection_generate if needed." };
    }
    public static void Verify(JObject before, JObject after, JObject plan)
    {
        var expected = (JObject)before.DeepClone();
        foreach (JToken row in plan["predicted"]!)
        {
            JToken original = expected["objects"]!.Single(o => PlacementEditPlanner.Id(o["locator"]!) == PlacementEditPlanner.Id(row["locator"]!));
            JToken actual = after["objects"]!.Single(o => PlacementEditPlanner.Id(o["locator"]!) == PlacementEditPlanner.Id(row["locator"]!));
            original["symbol"]!["variant"] = row["symbol"]!["variant"]!.DeepClone();
            original["locator"] = actual["locator"]!.DeepClone();
        }
        if (!JToken.DeepEquals(expected, after))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Symbol or position readback differs; the edit is rolled back."); }
        plan["predicted"] = new JArray(after["objects"]!.Where(o => plan["predicted"]!.Any(p => PlacementEditPlanner.Id(p["locator"]!) == PlacementEditPlanner.Id(o["locator"]!))).Select(o => o.DeepClone()));
    }
}

