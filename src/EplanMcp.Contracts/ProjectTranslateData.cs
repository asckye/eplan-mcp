namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ProjectTranslateExecute"/>: what EPLAN's translation changed, as read back.</summary>
public sealed class ProjectTranslateData
{
    /// <summary>The name of the project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The project's source language, the one translated from.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The project's translation languages, in stored order.</summary>
    public List<string> Languages { get; set; } = [];

    /// <summary>How many function texts and page descriptions in the scope were read.</summary>
    public int TextsChecked { get; set; }

    /// <summary>How many of them EPLAN changed.</summary>
    public int TextsChanged { get; set; }

    /// <summary>The first texts EPLAN changed, at most 50, in the order read.</summary>
    public List<TranslatedText> Changed { get; set; } = [];

    /// <summary>How many texts with a text in the source language still miss a translation language.</summary>
    public int StillMissing { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
