using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditPredictions
{
    internal static JObject Plan(JObject input, JObject before, JArray selected)
        => throw GraphicEditPlanner.Invalid("Graphic edit initialization is incomplete; the tool is not registered.");
    internal static bool Coordinates(JToken? value, int count) => value is JArray array && array.Count == count && array.All(n => n.Type is JTokenType.Integer or JTokenType.Float && !double.IsNaN((double)n) && !double.IsInfinity((double)n));
}
