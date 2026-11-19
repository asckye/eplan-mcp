using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public interface IPlacementEditor : IPlacementMover
{
    public JObject Inspect(string project, string page, bool symbolEdit = false, bool rename = false);
    public IReadOnlyDictionary<string, int>? InterruptionCounts(string projectPath);
    public Action PrepareEdit(JObject input, JObject before);
}
