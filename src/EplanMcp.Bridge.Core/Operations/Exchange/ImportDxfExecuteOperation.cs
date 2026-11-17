using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ImportDxfExecute"/>: checks the arguments, the file and the plan's fingerprint again, then
/// imports the drawing onto the page in one transaction and counts what it added; nothing added rolls the import back
/// (docs/tools/eplan_import_dxf.md, section 5). S22 found the import taken back by the SafetyPoint and undone by one
/// Ctrl+Z after the commit (api-notes A29).
/// </summary>
public sealed class ImportDxfExecuteOperation(IProjectCatalog catalog, IPageReader pages, IDrawingImporter importer, ITransactionScopeFactory transactions)
    : Operation<ImportDxfArgs, ImportDxfData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_import_dxf".</summary>
    public const string Tool = "eplan_import_dxf";

    public override string Name => OperationNames.ImportDxfExecute;

    protected override ImportDxfData Execute(ImportDxfArgs args)
    {
        ImportDxfRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one import.dxf.plan returned.");
            }

            request = ImportDxfRequest.Check(args, catalog, pages, importer);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The objects on the page, or the file, changed after the plan was made, so nothing was imported. Call eplan_import_dxf again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<int> committed = TransactionRunner.Run(transactions, Tool, () => Import(request));
        return new ImportDxfData
        {
            Project = request.Project.Name,
            Page = request.Page.Name,
            File = request.Source.File,
            Sha256 = request.Source.Sha256,
            ObjectsAdded = committed.Result,
            Warning = committed.Warning,
        };
    }

    private int Import(ImportDxfRequest request)
    {
        int added = importer.ImportDxf(request.Project.Path, request.Page.Name, request.Source.FullPath, request.FitToPage, request.Centered);
        return added > 0
            ? added
            : throw new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN added nothing to the page from '{request.Source.File}', so nothing was changed: the file may hold no drawing EPLAN can read. Ask the engineer to open it in a CAD program, or to import it in EPLAN by hand to see EPLAN's messages.",
                retryable: false,
                new Dictionary<string, string> { ["file"] = request.Source.File });
    }
}
