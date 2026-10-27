using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Imports once, diffs Comment objects and rolls back no-ops so no empty undo step remains (S44).</summary>
public sealed class ImportPdfCommentsExecuteOperation(IProjectCatalog catalog, IPdfCommentsImporter importer, ITransactionScopeFactory transactions, ISystemMessageReader? messages = null)
    : Operation<ImportPdfCommentsArgs, ImportPdfCommentsData>, IWriteOperation
{
    public const string Tool = "eplan_import_pdf_comments";

    public override string Name => OperationNames.ImportPdfCommentsExecute;

    protected override ImportPdfCommentsData Execute(ImportPdfCommentsArgs args)
    {
        ImportPdfCommentsRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one import.pdf_comments.plan returned.");
            }

            request = ImportPdfCommentsRequest.Check(args, catalog, importer);
            if (!string.Equals(request.Fingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale,
                    "The PDF or existing comments changed after planning. Call eplan_import_pdf_comments again for a new plan.");
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        int bookmark = messages?.ReadAll().Count ?? 0;
        Committed<ImportPdfCommentsData> result;
        try
        {
            result = TransactionRunner.Run(transactions, Tool, () => Import(request), data => data.Added > 0);
        }
        catch (BridgeErrorException ex)
        {
            var failed = new ImportPdfCommentsData();
            CollectMessages(failed, bookmark);
            ex.Error.Metadata ??= [];
            ex.Error.Metadata["eplanMessages"] = Protocol.BridgeJson.FromObject(failed.EplanMessages).ToString(Newtonsoft.Json.Formatting.None);
            ex.Error.Metadata["eplanMessageCount"] = failed.EplanMessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (failed.Warning is not null)
            { ex.Error.Metadata["messagesWarning"] = failed.Warning; }
            throw;
        }

        result.Result.Warning = result.Warning;
        CollectMessages(result.Result, bookmark);
        return result.Result;
    }

    private ImportPdfCommentsData Import(ImportPdfCommentsRequest request)
    {
        if (!importer.Import(request.Project.Path, request.Source.FullPath))
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument,
                "EPLAN rejected the PDF. It must be exported by EPLAN from this project and annotated in Adobe Acrobat/Reader. Only sticky-note (Text) annotations import; carets and underlines are ignored.");
        }

        IReadOnlyList<string> after = importer.Identifiers(request.Project.Path);
        var before = new HashSet<string>(request.Ids, StringComparer.Ordinal);
        if (!before.IsSubsetOf(after))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Existing comments disappeared during import; the import was rolled back. Ask the engineer to check EPLAN's messages.");
        }

        string[] added = [.. after.Where(id => !before.Contains(id))];
        return new ImportPdfCommentsData
        {
            Project = request.Project.Name,
            Before = before.Count,
            After = after.Count,
            Added = added.Length,
            Comments = importer.Read(request.Project.Path, added.Take(200).ToArray())
        };
    }

    private void CollectMessages(ImportPdfCommentsData result, int bookmark)
    {
        try
        {
            SystemMessageRecord[] added = [.. (messages?.ReadAll() ?? []).Skip(bookmark).Where(m => MessageListOperation.LevelName(m.Level) is not null)];
            result.EplanMessageCount = added.Length;
            result.EplanMessages = [.. added.Take(20).Select(m => new SystemMessage { Level = MessageListOperation.LevelName(m.Level)!, Number = m.Index.Length == 0 || m.Index == MessageListOperation.NoNumber ? null : m.Index, Text = m.Text, Occurrences = m.Occurrences })];
        }
        catch (Exception)
        {
            // A11: collection failures must not replace a known execution outcome.
            result.Warning += " EPLAN messages could not be read; inspect session messages in EPLAN.";
        }
    }
}
