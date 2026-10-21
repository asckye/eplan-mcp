using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.LanguageSetExecute"/>: checks the arguments and the plan's fingerprint again, then in one
/// transaction adds the languages the project lacks and sets the displayed languages, and reads both settings back
/// (docs/tools/eplan_language_set.md, section 5). A read-back that differs from the request is EPLAN_OPERATION_FAILED.
/// On every failure <see cref="LanguageTransaction"/> writes the settings back as they were, because the rollback
/// does not (api-notes A28).
/// </summary>
public sealed class LanguageSetExecuteOperation(IProjectCatalog catalog, IProjectLanguages languages, ITransactionScopeFactory transactions)
    : Operation<LanguageSetArgs, LanguageSetData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_language_set".</summary>
    public const string Tool = "eplan_language_set";

    /// <summary>How the engineer restores the settings when the write-back fails.</summary>
    internal const string Restore = "Ask the engineer to set the languages back in EPLAN's project settings (Translation).";

    public override string Name => OperationNames.LanguageSetExecute;

    protected override LanguageSetData Execute(LanguageSetArgs args)
    {
        LanguageSetRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one language.set.plan returned.");
            }

            request = LanguageSetRequest.Check(args, catalog, languages);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's languages changed after the plan was made, so nothing was changed. Call eplan_language_set again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string path = request.Project.Path;
        Committed<LanguageSettings> committed = LanguageTransaction.Run(transactions, languages, Tool, path, request.Before, Restore, () =>
        {
            foreach (string language in request.ToAdd)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                languages.Add(path, language);
            }

            OperationContext.Current.ThrowIfCancellationRequested();
            if (request.ClearsDisplay)
            {
                // SetDisplayedLanguages refuses "##_##;" (A28), so "not set" is written as the setting stores it.
                languages.Write(path, new LanguageSettings { Translation = languages.Read(path).Translation, Display = LanguageCodes.Stored([LanguageCodes.Unset]) });
            }
            else if (request.Display is { } display)
            {
                languages.SetDisplayed(path, LanguageCodes.ForDisplay(display));
            }

            LanguageSettings after = languages.Read(path);
            return request.Differs(after) is { } problem
                ? throw new BridgeErrorException(
                    ErrorReason.EplanOperationFailed,
                    "EPLAN did not set the languages as asked, so the language settings were written back as they were. metadata.problem says what EPLAN made of them.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name, ["problem"] = problem })
                : after;
        });
        return new LanguageSetData
        {
            Project = request.Project.Name,
            Before = LanguageCodes.Lists(request.Before),
            After = LanguageCodes.Lists(committed.Result),
            Warning = committed.Warning,
        };
    }
}
