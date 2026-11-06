using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanPageReviewReader
{
    internal static JObject ConnectionRow(CableDefinitionPlanner.Wire wire, JToken? from, JToken? to, IEnumerable<long> definitionPoints,
        string crossSection = "", string potentialName = "", string potentialType = "") => new()
        {
            ["from"] = from,
            ["to"] = to,
            ["locator"] = BridgeJson.FromObject(wire.Locator),
            ["drawnPath"] = wire.Path is null ? JValue.CreateNull() : JArray.FromObject(wire.Path),
            ["position"] = new JArray(wire.Points.FirstOrDefault(p => p.Length == 2) ?? []),
            ["definitionPoints"] = new JArray(definitionPoints),
            ["label"] = wire.Label,
            ["colour"] = wire.Colour,
            ["crossSection"] = crossSection,
            ["potentialName"] = potentialName,
            ["potentialType"] = potentialType,
        };

    internal static JObject CableRow(string designation, DrawingLocator locator, IEnumerable<long> members, JArray connections, double[]? location = null, double[][]? points = null)
    {
        var ids = new HashSet<long>(members);
        return new JObject
        {
            ["designation"] = designation,
            ["locator"] = BridgeJson.FromObject(locator),
            ["position"] = location is null ? null : new JArray(location),
            ["points"] = points is null ? null : JArray.FromObject(points),
            ["cores"] = new JArray(connections.Where(c => ids.Contains((long)c["locator"]!["oid"]!)).Select(c => c.DeepClone())),
        };
    }
}
