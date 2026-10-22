using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartSyncExecute"/>: checks the arguments and the plan's fingerprint again, backs the project
/// up (<see cref="ProjectBackups"/>; the Host has backed the parts database up before, ADR-0019), then lets EPLAN
/// synchronize in one project transaction and reads both back (docs/tools/eplan_part_sync.md, section 5). The
/// transaction's rollback takes back only the project; the parts database has no undo (S24). So a failure before the
/// synchronization is not_started (or rolled_back from the transaction itself), and every failure once it began is
/// OUTCOME_UNKNOWN. A read-back that does not match is reported, not an error.
/// </summary>
public sealed class PartSyncExecuteOperation(
    IProjectCatalog catalog,
    IPageReader pages,
    IPartReader reader,
    IPartsDatabaseWriter writer,
    IProjectBackup backup,
    ITransactionScopeFactory transactions)
    : Operation<PartSyncArgs, PartSyncData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_part_sync".</summary>
    public const string Tool = "eplan_part_sync";

    /// <summary>What the model reads for OUTCOME_UNKNOWN; the Host adds both backups to the metadata.</summary>
    public const string UnknownMessage =
        "EPLAN failed while synchronizing the parts. The project was rolled back where EPLAN could, but the parts database may be partly changed, and nothing rolls it back. Do not call again. Ask the engineer to check the parts in EPLAN's parts management and the project, and if needed to restore the backups made before (metadata.backup for the parts database, metadata.projectBackup for the project).";

    public override string Name => OperationNames.PartSyncExecute;

    protected override PartSyncData Execute(PartSyncArgs args)
    {
        PartSyncRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one part.sync.plan returned.");
            }

            request = PartSyncRequest.Check(args, catalog, reader, writer);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's parts or the parts database changed after the plan was made, so nothing was synchronized. Call eplan_part_sync again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        bool started = false;
        Committed<bool> committed;
        try
        {
            committed = TransactionRunner.Run(transactions, Tool, () =>
            {
                started = true;
                writer.Synchronize(request.Project.Path, request.Mode);
                return true;
            });
        }
        catch (Exception ex) when (started)
        {
            var unknown = new BridgeErrorException(ErrorReason.OutcomeUnknown, UnknownMessage, ex);
            unknown.Error.Execution = ExecutionState.Unknown;
            unknown.Error.Metadata = new Dictionary<string, string> { ["project"] = request.Project.Name };
            throw unknown;
        }

        return ReadBack(request, committed.Warning);
    }

    /// <summary>The plan's classification, the database and the project read back after the commit.</summary>
    private PartSyncData ReadBack(PartSyncRequest request, string? warning)
    {
        const int Max = PartImportRequest.MaxListed;
        var data = new PartSyncData
        {
            Project = request.Project.Name,
            Database = request.Database.Database,
            Added = [.. request.Added.Take(Max)],
            AddedCount = request.Added.Count,
            Overwritten = [.. request.Overwritten.Take(Max)],
            OverwrittenCount = request.Overwritten.Count,
            StoredInProject = [.. request.StoredInProject.Take(Max)],
            StoredInProjectCount = request.StoredInProject.Count,
            PartsBefore = request.PartCount,
            PartsExpected = request.PartCount + request.AddedVariants,
            ProjectPartsBefore = request.ProjectPartCount,
            Warning = warning,
        };
        try
        {
            PartsDatabaseState after = writer.Read();
            data.PartsAfter = after.PartCount;
            data.Missing = [.. request.Added.Concat(request.Overwritten).Where(number => !after.PartNumbers.Contains(number)).Take(Max)];
            ProjectParts parts = reader.ReadParts(request.Project.Path, null);
            var stored = new HashSet<string>(parts.Articles.Select(a => a.PartNumber), StringComparer.Ordinal);
            data.ProjectPartsAfter = parts.Articles.Count;
            data.NotStored = [.. request.StoredInProject.Where(number => !stored.Contains(number)).Take(Max)];
        }
        catch (Exception)
        {
            // The synchronization is committed; only the check is missing.
            data.Warning = (warning is null ? string.Empty : warning + " ") + "EPLAN synchronized the parts, but the parts database or the project could not be read back; ask the engineer to check them in EPLAN.";
        }

        return data;
    }
}
