namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Read access to the translation database EPLAN is configured with (docs/tools/eplan_translation_keyword.md),
/// implemented by the EPLAN adapter layer. Main thread only, inside the dispatcher's LockingStep. Matching, sorting and
/// paging stay in Bridge.Core, where they can be tested without EPLAN.
/// </summary>
public interface ITranslationDatabase
{
    /// <summary>
    /// Every keyword whose source text contains <paramref name="query"/>, with its translations, read from the
    /// configured database. Opens it read-only and closes it again before returning; never changes, creates or selects
    /// a database, and never changes the setting that names it. A database that cannot be opened or read throws
    /// EPLAN_OPERATION_FAILED; one on SQL Server throws CAPABILITY_MISSING, because only a file is opened by path.
    /// </summary>
    public TranslationDatabaseContent Read(string query);
}

/// <summary>What one read of the translation database returned.</summary>
public sealed class TranslationDatabaseContent
{
    /// <summary>The database as EPLAN's setting names it, path variables unexpanded.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The language codes the database holds.</summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>The keywords found, in the order EPLAN returned them.</summary>
    public List<Contracts.TranslationKeywordItem> Keywords { get; set; } = [];
}
