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
        ConnectionCreatePlanner.Scene scene, List<SymbolSearchItem> variants, bool existing = false, IPartsDatabase? parts = null)
    {
        ConnectionCreatePlanner.Members(value, at, existing ? "kind position" : "", "position symbol symbol_variant color cross_section designation part part_variant part_count");
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
        Require(properties.Count > 0 || value["part"] is not null, "", "Supply color, cross_section, designation or part.");
        JObject? article = null;
        if (value["part"] is { } part)
        {
            Require(part.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)part) && ((string)part!).Length <= 200 && !((string)part!).Any(char.IsControl), "/part", "Supply a nonempty part number within 200 characters.");
            Require(value["part_variant"] is null || value["part_variant"]!.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)value["part_variant"]), "/part_variant", "Supply a nonempty variant.");
            Require(value["part_count"] is null || value["part_count"]!.Type == JTokenType.Integer && long.TryParse(value["part_count"]!.ToString(), out long count) && count is >= 1 and <= uint.MaxValue, "/part_count", "Supply an integer count in 1..4294967295.");
            string number = (string)part!, variantName = (string?)value["part_variant"] ?? "1";
            var available = (parts ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The parts database reader is unavailable.")).Read(null).Parts;
            var matches = available.Where(p => p.PartNumber == number).ToArray();
            if (matches.Length == 0)
            { matches = available.Where(p => string.Equals(p.PartNumber, number, StringComparison.OrdinalIgnoreCase)).ToArray(); }
            if (matches.Length == 0)
            { throw new BridgeErrorException(ErrorReason.PartNotFound, "No matching part exists; use eplan_part_search to find the part number."); }
            Require(matches.Select(p => p.PartNumber).Distinct(StringComparer.Ordinal).Count() == 1, "/part", "Supply the exact part number.");
            Require(matches.Any(p => p.Variant == variantName), "/part_variant", "Select an existing part variant.");
            article = new JObject { ["part"] = matches[0].PartNumber, ["variant"] = variantName, ["count"] = (uint?)value["part_count"] ?? 1u };
        }
        else
        { Require(value["part_variant"] is null && value["part_count"] is null, "/part", "Supply part with its variant or count."); }
        if (properties["cross_section"] is { } section)
        { Require(Number(section, out _), "/cross_section", "Supply finite numeric text, for example 1.5 or 1,5."); }
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
        IEnumerable<double[][]> segments = existing ? scene.Graphical : path.Zip(path.Skip(1), (a, b) => new[] { a, b });
        double Distance(double[][] segment) => Enumerable.Range(0, 2).Sum(i => Math.Pow(point[i] - Math.Max(Math.Min(point[i], Math.Max(segment[0][i], segment[1][i])), Math.Min(segment[0][i], segment[1][i])), 2));
        double[][]? nearest = segments.OrderBy(Distance).FirstOrDefault();
        string hint = existing ? " Nearest existing segment: " + (nearest is null ? "none" : JArray.FromObject(nearest).ToString(Newtonsoft.Json.Formatting.None)) + "." : "";
        Require(segments.Any(s => ConnectionCreatePlanner.On(point, s)), "/position", "Place the definition point on a segment of this connection." + hint);
        if (existing)
        {
            ConnectionRecord[] owners = scene.Logical.Where(c => c.StartDevice is not null && c.EndDevice is not null
                && scene.Pins.TryGetValue(c.StartDevice, out SymbolConnectionPoint? start) && scene.Pins.TryGetValue(c.EndDevice, out SymbolConnectionPoint? end)
                && CableDefinitionPlanner.DrawnPath(scene, start.Location, end.Location) is { } route
                && route.Zip(route.Skip(1), (a, b) => new[] { a, b }).Any(s => ConnectionCreatePlanner.On(point, s))).ToArray();
            Require(owners.Length == 1, "/position", "Select a point on one generated existing connection; generate existing autoconnect lines first if needed." + hint);
            from = owners[0].StartDevice!;
            to = owners[0].EndDevice!;
        }
        return new JObject
        {
            ["itemIndex"] = index,
            ["endpoints"] = new JArray(from, to),
            ["type"] = "ConnectionDefinitionPoint",
            ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = name, ["variant"] = variant },
            ["position"] = new JArray(point),
            ["properties"] = properties,
            ["articles"] = article is null ? null : new JArray(article)
        };
    }

    internal static JObject PlanPotential(JToken value, string at, double[] position, int index)
    {
        ConnectionCreatePlanner.Members(value, at, "kind position", "potential_name signal_name");
        var properties = new JObject();
        foreach (string field in new[] { "potential_name", "signal_name" })
        {
            if (value[field] is not { } name)
            { continue; }
            ConnectionCreatePlanner.Require(name.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)name)
                && ((string)name!).Length <= 200 && !((string)name!).Any(char.IsControl), at + "/" + field, "Supply nonempty text within 200 characters.");
            properties[field] = name.DeepClone();
        }
        ConnectionCreatePlanner.Require(properties.Count > 0, at, "Supply potential_name or signal_name.");
        return new JObject { ["itemIndex"] = index, ["type"] = "PotentialDefinition", ["position"] = new JArray(position), ["properties"] = properties };
    }

    private static bool Number(JToken? value, out double number)
    {
        number = 0;
        return value?.Type == JTokenType.String
            && double.TryParse(value.Value<string>()!.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && !double.IsNaN(number) && !double.IsInfinity(number);
    }

    internal static JArray Verify(JArray planned, JArray actual)
    {
        void Match(JToken? expected, JToken? observed, string field, bool numeric = false)
        {
            if (!(numeric ? Number(expected, out double a) && Number(observed, out double b) && a == b : JToken.DeepEquals(expected, observed)))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, $"Definition point readback differs at {field}: requested {expected}, actual {observed}."); }
        }
        Match(new JValue(planned.Count), new JValue(actual.Count), "definitionPoints/count");
        foreach (JToken row in planned)
        {
            string at = "definitionPoints/" + row["itemIndex"];
            JToken? found = actual.SingleOrDefault(a => JToken.DeepEquals(a["itemIndex"], row["itemIndex"]));
            foreach (string field in new[] { "type", "symbol", "position" })
            { Match(row[field], found?[field], at + "/" + field); }
            if ((string?)row["type"] == "ConnectionDefinitionPoint")
            {
                Match(new JArray(row["endpoints"]!.Values<string>().OrderBy(p => p, StringComparer.Ordinal)),
                found?["endpoints"] is { } ends ? new JArray(ends.Values<string>().OrderBy(p => p, StringComparer.Ordinal)) : null, at + "/endpoints");
            }
            if (row["articles"] is JArray articles)
            {
                Match(articles, found?["articles"], at + "/articles");
                foreach (JToken article in articles)
                { if (found?["connectionArticles"]?.Any(a => JToken.DeepEquals(a, article)) != true) { Match(article, null, at + "/connectionArticles"); } }
            }
            foreach (JProperty property in ((JObject)row["properties"]!).Properties())
            {
                Match(property.Value, found?["properties"]?[property.Name], at + "/properties/" + property.Name, property.Name == "cross_section");
                if ((string?)row["type"] == "ConnectionDefinitionPoint")
                { Match(property.Value, found?["connectionValues"]?[property.Name], at + "/connectionValues/" + property.Name, property.Name == "cross_section"); }
            }
        }
        return actual;
    }
}
