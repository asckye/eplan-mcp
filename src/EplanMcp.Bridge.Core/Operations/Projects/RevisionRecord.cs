namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One record of the revision log, <c>PROJ_REVISION_LOG_NAME/DESCRIPTION/USER/DATE[i]</c>, as read (api-notes A28).</summary>
public sealed class RevisionRecord
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The description in the requested language, or in a fallback language; null when it has none.</summary>
    public string? Description { get; set; }

    /// <summary>The fallback language the description was read in; null when it is the requested one.</summary>
    public string? DescriptionLanguage { get; set; }

    public string? User { get; set; }

    /// <summary>The date as its <c>ToString()</c> gives it: Unix seconds in S23 r3; null when not set.</summary>
    public string? Date { get; set; }
}
