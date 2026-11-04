using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

public interface IConnectionDefinitionPointWriter
{
    public Action PrepareDefinitionPoints(string project, string page, JArray planned);
    public JArray ReadDefinitionPoints(string project, string page, JArray planned);
}

internal static class ConnectionDefinitionPoints
{
    internal static JObject Plan(JToken value, string at, double[][] path, string from, string to, int index,
        ConnectionCreatePlanner.Scene scene, List<SymbolSearchItem> variants)
    {
        ConnectionCreatePlanner.Members(value, at, "", "position symbol symbol_variant color cross_section designation");
        void Require(bool condition, string field, string message) => ConnectionCreatePlanner.Require(condition, at + field, message);
        var properties = new JObject();
        foreach (string field in new[] { "color", "cross_section", "designation" })
        {
            if (value[field] is not { } text)
            { continue; }
            Require(text.Type == JTokenType.String && ((string)text!).Length <= 200 && !string.IsNullOrWhiteSpace((string?)text)
                && !((string)text!).Any(char.IsControl), "/" + field, "Supply nonempty text within 200 characters.");
            properties[field] = text.DeepClone();
        }
        Require(properties.Count > 0, "", "Supply at least one of color, cross_section or designation.");
        if (properties["cross_section"] is { } section)
        { Require(double.TryParse((string)section!, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number) && !double.IsInfinity(number), "/cross_section", "Supply finite numeric text, for example 1.5."); }
        Require(value["symbol"] is null || value["symbol"]!.Type == JTokenType.String, "/symbol", "Use CDP or CDPNG.");
        string name = (string?)value["symbol"] ?? "CDP";
        Require(name is "CDP" or "CDPNG", "/symbol", "Use CDP or CDPNG.");
        Require(value["symbol_variant"] is null || value["symbol_variant"]!.Type == JTokenType.Integer
            && int.TryParse(value["symbol_variant"]!.ToString(), out _), "/symbol_variant", "Supply an Int32 variant number.");
        int variant = (int?)value["symbol_variant"] ?? 0;
        Require(variants.Any(s => s.Library == "SPECIAL" && s.Name == name && s.Variant == variant), "/symbol_variant", "Select an available SPECIAL symbol variant.");
        double[] point;
        if (value["position"] is { } position)
        {
            Require(position is JArray { Count: 2 } a && a.All(n => n.Type is JTokenType.Integer or JTokenType.Float && (double)n is >= 0 and <= 10000), "/position", "Supply [x_mm,y_mm] in 0..10000 mm.");
            point = position.Values<double>().ToArray();
        }
        else
        { point = path[0].Zip(path[1], (a, b) => a == b ? a : DrawingCoordinates.Snap((a + b) / 2, scene.Page.GridSize, true)).ToArray(); }
        Require(path.Zip(path.Skip(1), (a, b) => new[] { a, b }).Any(s => ConnectionCreatePlanner.On(point, s)), "/position", "Place the definition point on a segment of this connection.");
        return new JObject
        {
            ["itemIndex"] = index,
            ["endpoints"] = new JArray(from, to),
            ["type"] = "ConnectionDefinitionPoint",
            ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = name, ["variant"] = variant },
            ["position"] = new JArray(point),
            ["properties"] = properties
        };
    }

    internal static JArray Verify(JArray planned, JArray actual)
    {
        void Match(JToken? expected, JToken? observed, string field)
        {
            if (!JToken.DeepEquals(expected, observed))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, $"Definition point readback differs at {field}: requested {expected}, actual {observed}."); }
        }
        Match(new JValue(planned.Count), new JValue(actual.Count), "definitionPoints/count");
        foreach (JToken row in planned)
        {
            string at = "definitionPoints/" + row["itemIndex"];
            JToken? found = actual.SingleOrDefault(a => JToken.DeepEquals(a["itemIndex"], row["itemIndex"]));
            foreach (string field in new[] { "type", "symbol", "position" })
            { Match(row[field], found?[field], at + "/" + field); }
            Match(new JArray(row["endpoints"]!.Values<string>().OrderBy(p => p, StringComparer.Ordinal)),
                found?["endpoints"] is { } ends ? new JArray(ends.Values<string>().OrderBy(p => p, StringComparer.Ordinal)) : null, at + "/endpoints");
            foreach (JProperty property in ((JObject)row["properties"]!).Properties())
            {
                Match(property.Value, found?["properties"]?[property.Name], at + "/properties/" + property.Name);
                Match(property.Value, found?["connectionValues"]?[property.Name], at + "/connectionValues/" + property.Name);
            }
        }
        return actual;
    }
}
