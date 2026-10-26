namespace EplanMcp.Contracts;

/// <summary>Translation import counts verified against exports before and after the import (api-notes A47).</summary>
public sealed class TranslationImportData
{
    /// <summary>The configured translation database written.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>The number of rows in the input file.</summary>
    public int Entries { get; set; }

    /// <summary>The language used to match original texts.</summary>
    public string ReferenceLanguage { get; set; } = string.Empty;

    /// <summary>Distinct entries newly present after import.</summary>
    public int Added { get; set; }

    /// <summary>Empty translation cells filled with the supplied text.</summary>
    public int Filled { get; set; }

    /// <summary>Existing translation cells retained despite different supplied text.</summary>
    public int Kept { get; set; }
}
