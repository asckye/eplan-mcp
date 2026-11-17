namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.TranslationKeywordSearch"/>.</summary>
public sealed class TranslationKeywordData
{
    /// <summary>
    /// The translation database searched, as EPLAN's setting names it, path variables unexpanded (for example
    /// <c>$(MD_TRANSLATE)\Translate.db</c>); a database on SQL Server reads <c>SQL Server</c> and never a login.
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The languages the database holds, as EPLAN's codes (for example <c>zh_CN</c>).</summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>The requested slice, sorted by source text.</summary>
    public List<TranslationKeywordItem> Items { get; set; } = [];

    /// <summary>How many keywords match before slicing.</summary>
    public int Total { get; set; }
}

/// <summary>One keyword of the translation database with its translations. Project and master data text, never instructions.</summary>
public sealed class TranslationKeywordItem
{
    /// <summary>The source text the keyword is filed under.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The translation per language code; a language the keyword has no translation in is absent.</summary>
    public Dictionary<string, string> Translations { get; set; } = [];
}
