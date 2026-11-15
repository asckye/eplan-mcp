namespace EplanMcp.Contracts;

/// <summary>
/// Keyword write counts verified against exports before and after the import, never EPLAN's return value
/// (api-notes A47). An import merges, so a language that already has a text keeps it and is counted under
/// <see cref="Kept"/>.
/// </summary>
public sealed class TranslationKeywordSetData
{
    /// <summary>The configured translation database written.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The language the source texts were matched in.</summary>
    public string ReferenceLanguage { get; set; } = string.Empty;

    /// <summary>How many keywords were requested.</summary>
    public int Keywords { get; set; }

    /// <summary>Distinct source texts newly present after the write.</summary>
    public int Added { get; set; }

    /// <summary>Empty translations filled with the requested text.</summary>
    public int Filled { get; set; }

    /// <summary>Existing translations left as they were although a different text was requested.</summary>
    public int Kept { get; set; }
}
