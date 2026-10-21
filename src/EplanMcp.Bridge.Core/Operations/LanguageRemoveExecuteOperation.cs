using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.LanguageRemoveExecute"/>: checks the arguments and the plan's fingerprint again, backs the
/// project up (<see cref="ProjectBackups"/>), then removes the languages one by one in one transaction and reads the
/// settings back after each; a language still among the translation languages is EPLAN_OPERATION_FAILED, whatever
/// EPLAN returned (docs/tools/eplan_language_remove.md, section 5). The rollback takes the translations back but not
/// the settings (spike S23, api-notes A28), so on every failure <see cref="LanguageTransaction"/> writes them back after it.
/// </summary>
public sealed class LanguageRemoveExecuteOperation(
    IProjectCatalog catalog,
    IPageReader pages,
    IProjectLanguages languages,
    ILanguageRemover remover,
    IProjectBackup backup,
    ITransactionScopeFactory transactions,
    IBridgeLog log)
    : Operation<LanguageRemoveArgs, LanguageRemoveData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_language_remove".</summary>
    public const string Tool = "eplan_language_remove";

    /// <summary>How the engineer restores the project when the write-back fails; the Host adds the backup's path.</summary>
    internal const string Restore = "Ask the engineer to set the languages back in EPLAN's project settings (Translation), or to open the backup copy (metadata.backup).";

    public override string Name => OperationNames.LanguageRemoveExecute;

    protected override LanguageRemoveData Execute(LanguageRemoveArgs args)
    {
        LanguageRemoveRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one language.remove.plan returned.");
            }

            request = LanguageRemoveRequest.Check(args, catalog, languages, remover);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's languages or texts in them changed after the plan was made, so nothing was removed. Call eplan_language_remove again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        string path = request.Project.Path;
        Committed<LanguageSettings> committed = LanguageTransaction.Run(transactions, languages, Tool, path, request.Before, Restore, () =>
        {
            OperationContext context = OperationContext.Current;
            LanguageSettings after = request.Before;
            for (int i = 0; i < request.Languages.Count; i++)
            {
                string language = request.Languages[i];
                context.ThrowIfCancellationRequested();
                context.ReportProgress(i, request.Languages.Count, "Removing languages");

                // RemoveProjectLanguage returns false also when it removed the language (A28, S24 r18): only the
                // settings read back tell, and what it returned goes to the log.
                bool returned = remover.Remove(path, language);
                after = languages.Read(path);
                bool removed = !LanguageCodes.Split(after.Translation).Contains(language, StringComparer.Ordinal);
                log.Write(BridgeLogLevel.Information, $"[{context.RequestId}] RemoveProjectLanguage({language}) returned {returned}; read back: {(removed ? "removed" : "still a translation language")}");
                if (!removed)
                {
                    throw new BridgeErrorException(
                        ErrorReason.EplanOperationFailed,
                        $"EPLAN did not remove {language}, so nothing was removed and the language settings were written back as they were. Ask the engineer to check the project's languages in EPLAN; eplan_message_list may show why.",
                        retryable: false,
                        new Dictionary<string, string> { ["project"] = request.Project.Name, ["notRemoved"] = language });
                }
            }

            return after;
        });
        return new LanguageRemoveData
        {
            Project = request.Project.Name,
            Removed = [.. request.Languages],
            TextsAffected = request.Affected.Count,
            Before = LanguageCodes.Lists(request.Before),
            After = LanguageCodes.Lists(committed.Result),
            Warning = committed.Warning,
        };
    }
}
