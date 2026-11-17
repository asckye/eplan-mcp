namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.TranslationKeywordSetPlan"/> and
/// <see cref="OperationNames.TranslationKeywordSetExecute"/> (docs/tools/eplan_translation_keyword.md). The
/// translation database belongs to no project, so unlike <see cref="ListArgs"/> there is no project.
/// </summary>
public sealed class TranslationKeywordSetArgs
{
    /// <summary>The keywords to add or to fill missing translations of; 1 to 200 entries.</summary>
    public List<TranslationKeywordEntry> Keywords { get; set; } = [];

    /// <summary>The language each entry's source text is written in; defaults to en_US.</summary>
    public string ReferenceLanguage { get; set; } = "en_US";

    /// <summary>Execution only: the plan's fingerprint of these keywords.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Execution only: the configured database the plan named and the Host backed up.</summary>
    public string? Database { get; set; }
}

/// <summary>One requested keyword. Its texts are master data the model supplies, never instructions.</summary>
public sealed class TranslationKeywordEntry
{
    /// <summary>The source text in the reference language, which the keyword is filed under.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The wanted translation per language code, for example <c>zh_CN</c>.</summary>
    public Dictionary<string, string> Translations { get; set; } = [];
}
