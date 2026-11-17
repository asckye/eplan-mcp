using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54/A57: move roots only; property placements follow without changing their relative offsets.</summary>
public interface IPlacementMover
{
    public ConnectionCreatePlanner.Scene Read(string project, string page);
    public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves);
}
