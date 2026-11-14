namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.TranslationKeywordSearch"/> (docs/tools/eplan_translation_keyword.md). The
/// translation database belongs to no project, so unlike <see cref="ListArgs"/> there is no project.
/// </summary>
public sealed class TranslationKeywordArgs
{
    /// <summary>Text, 1 to 200 characters, that a keyword's source text contains, ignoring case; required.</summary>
    public string? Query { get; set; }

    /// <summary>Match the whole source text instead of a part of it, ignoring case.</summary>
    public bool Exact { get; set; }

    /// <summary>Index of the first keyword to return, taken from the Host's cursor.</summary>
    public int Offset { get; set; }

    /// <summary>The most keywords to return.</summary>
    public int Limit { get; set; }
}
