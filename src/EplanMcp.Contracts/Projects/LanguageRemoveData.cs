namespace EplanMcp.Contracts;

/// <summary>
/// Result of <see cref="OperationNames.LanguageRemoveExecute"/>: the languages removed, how many texts had translations
/// in them, and the project's languages before and as read back after.
/// </summary>
public sealed class LanguageRemoveData
{
    /// <summary>The name of the project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The languages removed, in the order given.</summary>
    public List<string> Removed { get; set; } = [];

    /// <summary>How many function texts and page descriptions had a translation in one of them before.</summary>
    public int TextsAffected { get; set; }

    /// <summary>The languages before.</summary>
    public ProjectLanguages Before { get; set; } = new();

    /// <summary>The languages read back after; EPLAN may have changed the displayed languages too (api-notes A28).</summary>
    public ProjectLanguages After { get; set; } = new();

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
