using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55: bounded native stages; release property lists before returning and verify only after commit.</summary>
public interface ICableDefinitionWriter
{
    public (Action Create, Action Verify) PrepareCables(string project, string page, JObject request, JObject plan);
    public void BindCables(string project, string page, JObject request, JObject plan);
    public void VerifyBindings(string project, string page, JObject request, JObject plan);
    public void AssignCores(string project, string page, JObject request, JObject plan);
    public JArray ReadCores(string project, string page, JObject request, JObject plan);
}
