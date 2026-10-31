using System.Diagnostics;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Export execution, separate from Write routing so it never starts a write transaction.</summary>
public sealed class ActionRunOperation(int major, IProjectCatalog catalog, IActionRunner runner, ISystemMessageReader messages) : Operation<ActionRunArgs, ActionRunData>
{
    private Func<string?, string> CheckFolder { get; } = ExportFolderCheck.CheckEmpty;
    internal ActionRunOperation(int major, IProjectCatalog catalog, IActionRunner runner, ISystemMessageReader messages, Func<string?, string> checkFolder)
        : this(major, catalog, runner, messages) => CheckFolder = checkFolder;
    public override string Name => OperationNames.ActionRun;
    protected override ActionRunData Execute(ActionRunArgs args)
    {
        ActionRunRequest request = ActionRunRequest.Check(args, major, catalog, runner, "export");
        string folder = CheckFolder(args.Directory);
        string output = (string)request.Entry["output"]!;
        if (args.Entry == "backup/MASTERDATA")
        { request.Data.Parameters.Add(new ActionParameter { Name = "DESTINATIONPATH", Value = folder }); }
        else
        { ActionParameter file = request.Data.Parameters.Single(p => p.Name == output); file.Value = Path.Combine(folder, file.Value); }
        return Run(request, runner, messages);
    }

    internal static ActionRunData Run(ActionRunRequest request, IActionRunner runner, ISystemMessageReader messages, bool unverified = false)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        OperationContext.Current.ReportProgress(0, message: "Running whitelisted action");
        // Folder/transaction preparation is finished: bookmark immediately before the action (A11).
        int bookmark = messages.ReadAll().Count;
        var elapsed = Stopwatch.StartNew();
        Exception? failure = null;
        bool done = false;
        var parameters = request.Data.Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        try
        { done = runner.Run((string)request.Entry["action"]!, parameters, unverified); }
        catch (Exception ex) { failure = ex; }
        if (unverified && failure is BridgeErrorException { Error.Reason: ErrorReason.OutcomeUnknown })
        { throw failure; }
        List<SystemMessageRecord> added;
        try
        { added = [.. messages.ReadAll().Skip(bookmark)]; }
        catch (Exception ex) { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN action messages could not be checked; inspect the session messages in EPLAN.", ex); }
        request.Data.EplanMessageCount = added.Count;
        request.Data.EplanMessages = [.. added.Take(20).Select(m => m.Index + ": " + m.Text)];
        if (!done || failure is not null || added.Any(m => m.Level is 4 or 5) || elapsed.Elapsed.TotalSeconds > (int)request.Entry["seconds"]!)
        {
            string message = "EPLAN could not complete the " + (unverified ? "trial" : "whitelisted") + " action; check this call's EPLAN messages.";
            var error = failure is null ? new BridgeErrorException(ErrorReason.EplanOperationFailed, message) : new BridgeErrorException(ErrorReason.EplanOperationFailed, message, failure);
            error.Error.Metadata = new Dictionary<string, string> { ["eplanMessages"] = JsonConvert.SerializeObject(request.Data.EplanMessages) };
            throw error;
        }
        OperationContext.Current.ThrowIfCancellationRequested();
        request.Data.After = runner.Inspect((string)request.Entry["entry"]!, request.Project?.Path, parameters).Before;
        OperationContext.Current.ThrowIfCancellationRequested();
        if (elapsed.Elapsed.TotalSeconds > (int)request.Entry["seconds"]!)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Action fingerprint readback exceeded its execution budget."); }
        request.Data.Warning += "Changes have not been checked item by item; use read tools to verify objects you care about. " + (string)request.Entry["effects"]!;
        return request.Data;
    }
}
