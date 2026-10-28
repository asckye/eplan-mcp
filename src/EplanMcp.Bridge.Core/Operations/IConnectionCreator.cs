using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A52/S40: discover before writing; read actual pin identities; compensate only the current project's placement.</summary>
public interface IConnectionCreator : ISymbolReader
{
    public ConnectionCreatePlanner.Scene Read(string project, string page);
    public Action Prepare(string project, string page, JArray symbols);
    public List<string?[]> Endpoints(string project, string page);
    public void Undo(string project);
}
