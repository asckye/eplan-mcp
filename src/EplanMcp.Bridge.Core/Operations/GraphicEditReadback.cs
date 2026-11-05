using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

internal static class GraphicEditReadback
{
    internal static void Stable(JToken before, JToken after, string operation, JObject plan)
        => throw new InvalidOperationException("Graphic readback initialization is incomplete; the tool is not registered.");
    internal static void Created(JToken expected, JToken actual, JObject plan)
        => throw new InvalidOperationException("Graphic readback initialization is incomplete; the tool is not registered.");
}
