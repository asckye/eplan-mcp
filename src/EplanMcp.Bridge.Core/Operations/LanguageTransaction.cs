using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Runs a change of a project's languages in one transaction (<see cref="TransactionRunner"/>) and writes the language
/// settings back when it does not commit. SafetyPoint takes the project's data back but not its settings (spike S23,
/// api-notes A28), so after a rollback the settings could keep the change: the languages set, or a removed language
/// missing while its translations are back. The write-back follows the rollback (docs/tools/eplan_language_set.md and
/// eplan_language_remove.md, section 5) and is read back; when it fails or reads back otherwise, the outcome is unknown
/// (OUTCOME_UNKNOWN), and the error holds the settings as they were. After a commit or rollback that itself failed the
/// outcome is unknown already: nothing is written, the settings are only named. Main thread only.
/// </summary>
public static class LanguageTransaction
{
    /// <summary>
    /// <see cref="TransactionRunner.Run"/> for <paramref name="change"/>, then the write-back of
    /// <paramref name="before"/> on every failure. <paramref name="restore"/> tells the model how the engineer gets the
    /// settings back when the write-back fails.
    /// </summary>
    public static Committed<T> Run<T>(ITransactionScopeFactory transactions, IProjectLanguages languages, string tool, string projectPath, LanguageSettings before, string restore, Func<T> change)
    {
        try
        {
            return TransactionRunner.Run(transactions, tool, change);
        }
        catch (BridgeErrorException unknown) when (unknown.Error.Execution == ExecutionState.Unknown)
        {
            Name(unknown.Error, before);
            throw;
        }
        catch (Exception failure)
        {
            WriteBack(languages, projectPath, before, tool, restore, failure);
            throw;
        }
    }

    /// <summary>
    /// True when <paramref name="now"/> holds the same languages in the same order as <paramref name="before"/>, and the
    /// same source language.
    /// </summary>
    public static bool Same(LanguageSettings now, LanguageSettings before) =>
        LanguageCodes.Split(now.Translation).SequenceEqual(LanguageCodes.Split(before.Translation), StringComparer.Ordinal)
        && LanguageCodes.Split(now.Display).SequenceEqual(LanguageCodes.Split(before.Display), StringComparer.Ordinal)
        && LanguageCodes.SourceOf(now) == LanguageCodes.SourceOf(before);

    /// <summary>Returns when the settings are as before, written back or never changed; otherwise throws OUTCOME_UNKNOWN.</summary>
    private static void WriteBack(IProjectLanguages languages, string projectPath, LanguageSettings before, string tool, string restore, Exception failure)
    {
        Exception cause = failure;
        try
        {
            if (Same(languages.Read(projectPath), before))
            {
                return;
            }

            languages.Write(projectPath, before);
            if (Same(languages.Read(projectPath), before))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            cause = ex;
        }

        var error = new BridgeErrorException(
            ErrorReason.OutcomeUnknown,
            $"{tool} failed, and the project's language settings could not be written back as they were, so they may differ from before. Do not call this tool again. {restore} metadata.translation, metadata.display and metadata.source hold the settings as they were.",
            cause);
        error.Error.Execution = ExecutionState.Unknown;
        Name(error.Error, before);
        throw error;
    }

    private static void Name(BridgeError error, LanguageSettings before)
    {
        error.Metadata ??= [];
        error.Metadata["translation"] = string.Join("\n", LanguageCodes.Split(before.Translation));
        error.Metadata["display"] = string.Join("\n", LanguageCodes.Split(before.Display));
        error.Metadata["source"] = LanguageCodes.SourceOf(before);
    }
}
