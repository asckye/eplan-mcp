using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartImportExecute"/>: checks the arguments and the plan's fingerprint again (the file and
/// which of its part numbers the database has), then imports the file into the configured parts database and reads it
/// back (docs/tools/eplan_part_import.md, section 5). The Host has backed the database up before (masterdata.backup,
/// ADR-0019). No transaction: in one, the import leaves an undo log open and is not rolled back anyway (S24 r11). So
/// every failure before the import is not_started, and every failure of the import itself is OUTCOME_UNKNOWN, since the
/// database may be partly changed and nothing rolls it back. A read-back that does not match is reported, not an error:
/// the import ran (tool spec 5).
/// </summary>
public sealed class PartImportExecuteOperation(IPartsDatabaseWriter writer) : Operation<PartImportArgs, PartImportData>, IWriteOperation
{
    /// <summary>What the model reads for OUTCOME_UNKNOWN; the Host adds the backup as <c>metadata.backup</c>.</summary>
    public const string UnknownMessage =
        "EPLAN reported an error while importing the parts, so the parts database may be partly changed, and an import cannot be rolled back. Do not call again. Ask the engineer to check the parts in EPLAN's parts management and, if needed, to restore the backup made before the import (metadata.backup).";

    public override string Name => OperationNames.PartImportExecute;

    protected override PartImportData Execute(PartImportArgs args)
    {
        PartImportRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one part.import.plan returned.");
            }

            request = PartImportRequest.Check(args, writer);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The file or the parts database changed after the plan was made, so nothing was imported. Call eplan_part_import again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["file"] = request.Source.File });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        // The last safe point: a cancel now leaves the database as it was.
        OperationContext.Current.ThrowIfCancellationRequested();
        try
        {
            writer.Import(request.Source.FullPath, request.Mode, request.MatchBy);
        }
        catch (Exception ex)
        {
            var unknown = new BridgeErrorException(ErrorReason.OutcomeUnknown, UnknownMessage, ex);
            unknown.Error.Execution = ExecutionState.Unknown;
            unknown.Error.Metadata = new Dictionary<string, string> { ["file"] = request.Source.File };
            throw unknown;
        }

        return ReadBack(request);
    }

    /// <summary>The plan's classification and the database read back: every added or updated part there, and the count.</summary>
    private PartImportData ReadBack(PartImportRequest request)
    {
        var data = new PartImportData
        {
            Database = request.Database.Database,
            Added = [.. request.Added.Take(PartImportData.MaxListed)],
            AddedCount = request.Added.Count,
            Updated = [.. request.Updated.Take(PartImportData.MaxListed)],
            UpdatedCount = request.Updated.Count,
            Skipped = [.. request.Skipped.Take(PartImportData.MaxListed)],
            SkippedCount = request.Skipped.Count,
            PartsBefore = request.PartCount,
            PartsExpected = request.PartCount + request.AddedVariants,
        };
        try
        {
            PartsDatabaseState after = writer.Read();
            data.PartsAfter = after.PartCount;
            data.Missing = [.. request.Added.Concat(request.Updated).Where(number => !after.PartNumbers.Contains(number)).Take(PartImportData.MaxListed)];
        }
        catch (Exception)
        {
            // The import has run; only the check is missing.
            data.Warning = "EPLAN imported the file, but the parts database could not be read back; ask the engineer to check the parts in EPLAN's parts management.";
        }

        return data;
    }
}
