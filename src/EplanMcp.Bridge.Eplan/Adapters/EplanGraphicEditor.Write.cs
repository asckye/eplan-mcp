using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanGraphicEditor
{
    public Action PrepareEdit(JObject input, JObject before, JObject plan)
        => throw new InvalidOperationException("Native graphic edit initialization is incomplete; the tool is not registered.");
}
