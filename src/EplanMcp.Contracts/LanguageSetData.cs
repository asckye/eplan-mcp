namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.LanguageSetExecute"/>: the project's languages before and as read back after.</summary>
public sealed class LanguageSetData
{
    /// <summary>The name of the project.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The languages before the change.</summary>
    public ProjectLanguages Before { get; set; } = new();

    /// <summary>The languages read back after the change.</summary>
    public ProjectLanguages After { get; set; } = new();

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
