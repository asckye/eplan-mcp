using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public interface IPlacementEditor : IPlacementMover
{
    public JObject Inspect(string project, string page);
    public Action PrepareEdit(JObject input, JObject before);
}
