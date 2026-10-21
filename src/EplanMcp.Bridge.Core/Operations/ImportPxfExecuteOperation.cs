using System.Diagnostics;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ImportPxfExecute"/>: checks the file, the name and the plan's fingerprint again, then lets
/// EPLAN import the EPJ file as a new project at the target and checks that its link file and data folder exist
/// (docs/tools/eplan_import_pxf.md, section 5; api-notes A29). There is no transaction: on any failure, a cancel during
/// the import included, it deletes the files the import made, which the plan found absent, and reports
/// <c>rolled_back</c>; when they cannot all be deleted the outcome is unknown, since an incomplete project remains.
/// </summary>
public sealed class ImportPxfExecuteOperation(IProjectImporter importer) : Operation<ImportPxfArgs, ImportPxfData>, IWriteOperation
{
    /// <summary>The tool this operation serves.</summary>
    public const string Tool = "eplan_import_pxf";

    public override string Name => OperationNames.ImportPxfExecute;

    protected override ImportPxfData Execute(ImportPxfArgs args)
    {
        ImportPxfRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one import.pxf.plan returned.");
            }

            request = ImportPxfRequest.Check(args);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The file changed after the plan was made, so nothing was imported. Call eplan_import_pxf again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["file"] = request.Source.File });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var clock = Stopwatch.StartNew();
        try
        {
            importer.ImportPxf(request.Source.FullPath, request.Target);
            if (!File.Exists(request.LinkFile) || !Directory.Exists(request.DataFolder))
            {
                throw new BridgeErrorException(
                    ErrorReason.EplanOperationFailed,
                    "EPLAN finished the import without making the new project's files, so no project was made.",
                    retryable: false,
                    new Dictionary<string, string> { ["file"] = request.Source.File });
            }

            // The last safe point (M4 design 4.3): a cancel that came during the import takes the new project away again.
            OperationContext.Current.ThrowIfCancellationRequested();
        }
        catch (Exception failure)
        {
            throw Undone(request, failure);
        }

        return new ImportPxfData { Name = request.Name, Path = request.LinkFile, Seconds = (int)Math.Round(clock.Elapsed.TotalSeconds) };
    }

    /// <summary>
    /// What a failed import becomes once the files it made are deleted: its own error, rolled back; or, when they cannot
    /// all be deleted, OUTCOME_UNKNOWN. S22 found that EPLAN leaves an incomplete project behind when the import stops.
    /// </summary>
    private static Exception Undone(ImportPxfRequest request, Exception failure)
    {
        if (!ImportedProject.TryDelete(request.Target))
        {
            var unknown = new BridgeErrorException(
                ErrorReason.OutcomeUnknown,
                $"The import did not finish, and the files it made for project '{request.Name}' could not all be deleted, so an incomplete project may remain in the scratch folder's 'imported' folder. Do not call this tool again with this name; ask the engineer to delete that project's files.",
                failure);
            unknown.Error.Execution = ExecutionState.Unknown;
            return unknown;
        }

        if (failure is BridgeErrorException known)
        {
            known.Error.Execution = ExecutionState.RolledBack;
            known.Error.Message += " Any files the import had made were deleted again.";
        }

        return failure;
    }
}
