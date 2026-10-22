namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A placeholder's multi-language value: the text in the requested language (null when it has none), the language it
/// was read in when that is a fallback, and the value as stored with all its languages (<c>de_DE@…;en_US@…;</c>), which
/// only fingerprints use. Project data.
/// </summary>
public sealed class PlaceholderText
{
    public string? Text { get; set; }

    public string? Language { get; set; }

    public string? Raw { get; set; }
}
