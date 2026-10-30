using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55: bounded native stages; property lists are released before returning, including on failure.</summary>
public interface ICableDefinitionWriter
{
    public Action PrepareCables(string project, string page, JObject request, JObject plan);
    public void BindCables(string project, string page, JObject request, JObject plan);
    public void AssignCores(string project, string page, JObject request, JObject plan);
    public JArray ReadCores(string project, string page, JObject request, JObject plan);
}
