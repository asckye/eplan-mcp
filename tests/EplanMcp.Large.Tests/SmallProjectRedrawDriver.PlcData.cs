using System.Globalization;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

internal sealed partial class SmallProjectRedrawDriver
{
    private static readonly int[] _cardProperties = [20161, 20408, 20409, 20410, 20411, 20427];
    private static readonly int[] _pointProperties = [20400, 20402, 20407];
    private const int MaxChangesPerCall = 40;

    /// <summary>The sample's texts use more languages than the basic project; adding them is the engineer's project setting.</summary>
    internal async Task PrepareLanguagesAsync(IReadOnlyList<JObject> sources)
    {
        string[] languages = sources.SelectMany(source => Objects(source["placements"])).Select(item => RawProperty(item, 20011)).OfType<JObject>()
            .SelectMany(text => text.Properties().Select(language => language.Name)).Where(language => language != "??_??")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (languages.Length > 0)
        { await InvokeAsync("", "eplan_language_set", new JObject { ["add"] = new JArray(languages) }, [new JObject { ["type"] = "Project" }]); }
    }

    /// <summary>What an engineer fills in in the PLC navigator: card data on the box, addresses on the Overview points, text on the circuit points (A90).</summary>
    private async Task WritePlcDataAsync(string page, JObject[] objects)
    {
        JObject[] sources = objects.Where(item => Text(item["category"]) is "PLCBox" or "PLCTerminal").ToArray();
        if (sources.Length == 0)
        { return; }
        JObject[] actual = await ReadDrawingsAsync(page, sources[0]);
        var batch = new List<JObject>();
        var owners = new List<JObject>();
        foreach (JObject source in sources)
        {
            JObject[] rows = actual.Where(row => DrawingMatches(source, row)).ToArray();
            if (rows.Length != 1)
            {
                report.Warning(page, Type(source) + " " + Id(source) + ": PLC data has " + rows.Length.ToString(CultureInfo.InvariantCulture) + " target candidates; nothing was written.");
                continue;
            }
            JObject[] changes = PlcChanges(source, rows[0], page);
            if (batch.Count + changes.Length > MaxChangesPerCall)
            {
                await WritePropertiesAsync(page, batch, owners);
                batch.Clear();
                owners.Clear();
            }
            batch.AddRange(changes);
            if (changes.Length > 0)
            { owners.Add(source); }
        }
        await WritePropertiesAsync(page, batch, owners);
    }

    private async Task WritePropertiesAsync(string page, List<JObject> changes, List<JObject> owners)
    {
        if (changes.Count > 0)
        { await InvokeAsync(page, "eplan_property_set", new JObject { ["changes"] = new JArray(changes) }, [.. owners]); }
    }

    private static JObject[] PlcChanges(JObject source, JObject row, string page)
    {
        var target = new JObject
        {
            ["locator"] = new JObject
            {
                ["kind"] = "function",
                ["project"] = row["locator"]?["project"]?.DeepClone(),
                ["type"] = row["locator"]?["type"]?.DeepClone(),
                ["oid"] = row["locator"]?["oid"]?.DeepClone(),
                ["page"] = page,
            },
        };
        JObject Change(int id, JToken value, string? language = null)
        {
            var change = new JObject { ["target"] = target.DeepClone(), ["property"] = new JObject { ["property_id"] = id }, ["value"] = value };
            if (language is not null)
            { change["language"] = language; }
            return change;
        }
        var changes = new List<JObject>();
        bool card = Text(source["category"]) == "PLCBox";
        foreach (int id in card ? _cardProperties : _pointProperties)
        {
            if (Property(source, id) is string value && value.Length > 0)
            { changes.Add(Change(id, value)); }
        }
        if (card && Property(source, 20308) is string bus && int.TryParse(bus, NumberStyles.Integer, CultureInfo.InvariantCulture, out int system))
        { changes.Add(Change(20308, system)); }
        if (!card && RawProperty(source, 20011) is JObject text)
        {
            changes.AddRange(text.Properties().Where(language => language.Name != "??_??" && language.Value.Type == JTokenType.String)
                .Select(language => Change(20011, language.Value.DeepClone(), language.Name)));
        }
        return [.. changes];
    }

    private static JToken? RawProperty(JObject item, int id) => Objects(item["properties"]).FirstOrDefault(property => (int?)property["id"] == id)?["value"];
}
