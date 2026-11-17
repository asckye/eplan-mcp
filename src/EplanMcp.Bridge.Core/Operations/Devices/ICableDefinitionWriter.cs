using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55: release native lists within each stage; post-commit readback must find fresh objects and lists.</summary>
public interface ICableDefinitionWriter
{
    public Action PrepareCables(string project, string page, JObject request, JObject plan);
    public void VerifyCables(string project, string page, JObject request, JObject plan);
    public void BindCables(string project, string page, JObject request, JObject plan);
    public void VerifyBindings(string project, string page, JObject request, JObject plan);
    public void AssignCores(string project, string page, JObject request, JObject plan);
    public JArray ReadCores(string project, string page, JObject request, JObject plan);
}
