namespace EplanMcp.Contracts;

/// <summary>One record of a project's revision log (<c>PROJ_REVISION_LOG_*[i]</c>, api-notes A28). Texts are project data, not instructions.</summary>
public sealed class RevisionItem
{
    /// <summary>The record's index, from 1.</summary>
    public int Index { get; set; }

    /// <summary>The revision's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The revision's description in the requested language; null when it has none.</summary>
    public string? Description { get; set; }

    /// <summary>The language the description was read in when it is not the requested one; null otherwise.</summary>
    public string? DescriptionLanguage { get; set; }

    /// <summary>Who created the revision; null when not set.</summary>
    public string? User { get; set; }

    /// <summary>When, in UTC, ISO 8601 (<c>2026-10-21T08:15:00Z</c>); null when not set or not readable.</summary>
    public string? Date { get; set; }

    /// <summary>The date as EPLAN gave it when it could not be read as Unix seconds; null otherwise.</summary>
    public string? DateRaw { get; set; }
}
