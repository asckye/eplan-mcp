using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A52/S40: discover before writing; read actual pin identities; compensate only the current project's placement.</summary>
public interface IConnectionCreator : ISymbolReader
{
    public ConnectionCreatePlanner.Scene Read(string project, string page);
    public LayoutGuard.Model Measure(string project, string page);
    public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null);
    public List<string?[]> Endpoints(string project, string page);
    public void Undo(string project);
    public JObject Interruptions(string project, IReadOnlyList<string> pages);
    public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false);
}

/// <summary>Reacquires this call's symbols by saved identity after commit; never reads logical endpoints or pairing.</summary>
public interface IConnectionPlacementReadback
{
    public IReadOnlyList<InterruptionNameReadback> VerifyCreatedSymbols(string project, string page, JArray symbols);
}
