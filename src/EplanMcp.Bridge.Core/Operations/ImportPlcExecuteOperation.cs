using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Backs up, imports and verifies inside TransactionScope; verifies rollback too (tool spec sections 5 and 6).</summary>
public sealed class ImportPlcExecuteOperation(IProjectCatalog catalog, IPageReader pages, IPlcImporter importer, IProjectBackup backup, ITransactionScopeFactory transactions, ISystemMessageReader? messages = null)
    : Operation<ImportPlcArgs, ImportPlcData>, IWriteOperation
{
    /// <summary>The undo step's tool name; undo after commit remains unverified (S34, A39).</summary>
    public const string Tool = "eplan_import_plc";

    private readonly Action<ImportSource> _readSource = source => source.Read(hash: true, maxBytes: 100 * 1024 * 1024);
    private readonly Action<OpenProject, BackupTarget?> _makeBackup = (project, target) => ProjectBackups.Make(backup, pages, project, target);

    internal ImportPlcExecuteOperation(IProjectCatalog catalog, IPageReader pages, IPlcImporter importer, IProjectBackup backup, ITransactionScopeFactory transactions,
        Action<ImportSource> readSource, Action<OpenProject, BackupTarget?> makeBackup, ISystemMessageReader? messages = null) : this(catalog, pages, importer, backup, transactions, messages)
    {
        _readSource = readSource;
        _makeBackup = makeBackup;
    }

    public override string Name => OperationNames.ImportPlcExecute;

    protected override ImportPlcData Execute(ImportPlcArgs args)
    {
        int bookmark = -1;
        try
        {
            ImportPlcData result = Import(args, out bookmark);
            CollectMessages(result, bookmark);
            return result;
        }
        catch (BridgeErrorException ex)
        {
            var failed = new ImportPlcData();
            CollectMessages(failed, bookmark);
            ex.Error.Metadata ??= [];
            ex.Error.Metadata["eplanMessages"] = Protocol.BridgeJson.FromObject(failed.EplanMessages).ToString(Newtonsoft.Json.Formatting.None);
            ex.Error.Metadata["eplanMessageCount"] = failed.EplanMessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (failed.EplanMessageCount > failed.EplanMessages.Count)
            {
                ex.Error.Metadata["truncated"] = "true";
            }

            if (failed.Warning is not null)
            {
                ex.Error.Metadata["messagesWarning"] = failed.Warning;
            }

            throw;
        }
    }

    private void CollectMessages(ImportPlcData result, int bookmark)
    {
        try
        {
            if (bookmark < 0)
            {
                throw new InvalidOperationException("Message bookmark unavailable.");
            }

            List<SystemMessageRecord> added = [.. (messages?.ReadAll() ?? []).Skip(bookmark).Where(m => MessageListOperation.LevelName(m.Level) is not null)];
            result.EplanMessageCount = added.Count;
            result.EplanMessages = [.. added.Take(20).Select(m => new SystemMessage { Level = MessageListOperation.LevelName(m.Level)!, Number = m.Index.Length == 0 || m.Index == MessageListOperation.NoNumber ? null : m.Index, Text = m.Text, Occurrences = m.Occurrences })];
        }
        catch (Exception)
        {
            // A11: only copied records survive ReadAll; a collection failure cannot replace a known outcome.
            result.Warning += " EPLAN messages could not be read; inspect the session messages in EPLAN.";
        }
    }

    private ImportPlcData Import(ImportPlcArgs args, out int bookmark)
    {
        bookmark = -1;
        ImportPlcRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one import.plc.plan returned.");
            }

            OperationContext.Current.ThrowIfCancellationRequested();
            request = Checked(args);
            OperationContext.Current.ReportProgress(0, message: "Backing up project before PLC import");
            _makeBackup(request.Project, args.Backup);
            request = Checked(args);
            OperationContext.Current.ThrowIfCancellationRequested();
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        try
        {
            // After the backup: opening and closing the copy logs messages with local paths that are not this call's.
            bookmark = messages?.ReadAll().Count ?? 0;
        }
        catch (Exception)
        {
            // A missing bookmark must not change the execution outcome.
        }

        Committed<(PlcImportState After, int NewObjectsSharingNames)> committed;
        try
        {
            committed = TransactionRunner.Run(transactions, Tool, () =>
            {
                PlcImportState before = importer.Read(request.Project.Path);
                OperationContext.Current.ThrowIfCancellationRequested();
                OperationContext.Current.ReportProgress(1, message: "Importing PLC bus data");
                if (!importer.Import(request.Project.Path, request.Source.FullPath, request.Converter))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused the PLC import, so it was rolled back. Ask the engineer to check the file and EPLAN's messages; the backup was retained.");
                }

                OperationContext.Current.ThrowIfCancellationRequested();
                OperationContext.Current.ReportProgress(2, message: "Reading back PLC identities and counts");
                PlcImportState after = importer.Read(request.Project.Path);
                int sharing = ImportPlcRequest.Verify(before, after);
                return (after, sharing);
            });
        }
        catch (Exception failure) when (failure is not BridgeErrorException { Error.Execution: ExecutionState.Unknown })
        {
            try
            {
                if (!string.Equals(request.Before.StateFingerprint(), importer.Read(request.Project.Path).StateFingerprint(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The PLC import rollback did not restore the baseline.");
                }
            }
            catch (Exception ex)
            {
                var unknown = new BridgeErrorException(ErrorReason.OutcomeUnknown, "The PLC import failed and its rollback could not be verified. Do not import again; ask the engineer to check the project and retained backup.", ex);
                unknown.Error.Execution = ExecutionState.Unknown;
                throw unknown;
            }

            throw;
        }

        return new ImportPlcData
        {
            Project = request.Project.Name,
            File = request.Source.File,
            Bytes = request.Source.Bytes,
            Sha256 = request.Source.Sha256,
            Converter = request.Converter,
            Before = request.Before.Counts,
            After = committed.Result.After.Counts,
            AddedFunctions = committed.Result.After.Counts.Functions - request.Before.Counts.Functions,
            AddedPlcBoxes = committed.Result.After.Counts.PlcBoxes - request.Before.Counts.PlcBoxes,
            NewObjectsSharingNames = committed.Result.NewObjectsSharingNames,
            Warning = committed.Warning,
        };
    }

    private ImportPlcRequest Checked(ImportPlcArgs args)
    {
        var request = ImportPlcRequest.Check(args, catalog, importer, _readSource);
        return string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal)
            ? request
            : throw new BridgeErrorException(ErrorReason.PlanStale, "The file or project devices changed after planning, so nothing was imported. Call eplan_import_plc again for a new plan.");
    }
}
