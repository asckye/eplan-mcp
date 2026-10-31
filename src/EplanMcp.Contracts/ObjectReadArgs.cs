namespace EplanMcp.Contracts;
/// <summary>Read-only dictionary discovery or property continuation; positions are internal, never public cursors.</summary>
public sealed class ObjectReadArgs
{
    /// <summary>An open project name or resolved path.</summary>
    public string? Project { get; set; }
    /// <summary>The exact shipped dictionary type.</summary>
    public string ObjectType { get; set; } = string.Empty;
    /// <summary>An optional exact page filter.</summary>
    public string? Page { get; set; }
    /// <summary>An optional case-insensitive name substring.</summary>
    public string? NameContains { get; set; }
    /// <summary>The exact identity returned by discovery.</summary>
    public DrawingLocator? Locator { get; set; }
    /// <summary>An explicit language, or the GUI language with fallback.</summary>
    public string? Language { get; set; }
    /// <summary>Include property display and definition details.</summary>
    public bool Detailed { get; set; }
    /// <summary>Object or property selector continuation position.</summary>
    public int Offset { get; set; }
    /// <summary>Existing-index continuation position.</summary>
    public int IndexOffset { get; set; }
}
