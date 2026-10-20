using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.MacroInsertExecute"/>: checks the arguments and the plan's fingerprint again, then, in one
/// transaction, notes the project's main functions, inserts the macro and reads back; devices merged with existing ones
/// roll the insert back (docs/tools/eplan_macro_insert.md, section 5). The transaction's SafetyPoint is what takes an
/// insert back, and the insert is one undo step (api-notes A13, A14).
/// </summary>
public sealed class MacroInsertExecuteOperation(
    IProjectCatalog catalog,
    IPageReader pages,
    IDeviceReader devices,
    IMacroReader macros,
    IMacroInserter inserter,
    ITransactionScopeFactory transactions)
    : Operation<MacroInsertArgs, MacroInsertData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_macro_insert".</summary>
    public const string Tool = "eplan_macro_insert";

    public override string Name => OperationNames.MacroInsertExecute;

    protected override MacroInsertData Execute(MacroInsertArgs args)
    {
        MacroInsertRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one macro.insert.plan returned.");
            }

            request = MacroInsertRequest.Check(args, catalog, pages, macros, inserter);
            if (!string.Equals(request.StateFingerprint(devices), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The objects on the page, the macro file or, with numbering keep, the tags in use changed after the plan was made, so nothing was inserted. Call eplan_macro_insert again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<PlacedMacro> committed = TransactionRunner.Run(transactions, Tool, () => Insert(request));
        List<PageDevice> inserted = MacroInsertRequest.Devices(committed.Result.Functions);
        return new MacroInsertData
        {
            Project = request.Project.Name,
            Page = request.Page.Name,
            Macro = request.Macro,
            Devices = [.. inserted.Take(MacroInsertRequest.MaxDevices)],
            DeviceCount = inserted.Count,
            ObjectCount = committed.Result.ObjectCount,
            PageSizeKnown = request.Page.Width is not null,
            Warning = committed.Warning,
        };
    }

    /// <summary>The tool spec's steps in the transaction: note the main functions, insert, then refuse merged devices (S10).</summary>
    private PlacedMacro Insert(MacroInsertRequest request)
    {
        string[] before = MainFunctions(request);
        PlacedMacro placed = inserter.Insert(request.Project.Path, request.Page.Name, request.MacroPath, request.Variant, request.X, request.Y, request.Number);
        List<string> merged = MacroInsertRequest.Merged(before, MainFunctions(request));
        return merged.Count == 0
            ? placed
            : throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                request.Number
                    ? "EPLAN gave inserted devices the tags of existing devices, which merges them, so nothing was inserted. Ask the engineer to check the macro in EPLAN."
                    : "With numbering 'keep' the macro's devices took the tags of existing devices, which merges them in EPLAN, so nothing was inserted. Use numbering 'number', or first give the existing devices other tags with eplan_device_update.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = request.Project.Name, ["tags"] = string.Join("\n", merged.Take(ProjectResolver.MaxCandidates)) });
    }

    private string[] MainFunctions(MacroInsertRequest request) => [.. devices.ReadMainFunctionNames(request.Project.Path).Select(f => f.Name)];
}
