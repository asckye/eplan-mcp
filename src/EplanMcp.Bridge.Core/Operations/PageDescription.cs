namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A page description as stored: the raw value (<c>de_DE@…;zh_CN@…;</c>, api-notes A11; null when not set) and its
/// languages in stored order (<see cref="TextLanguages.Stored"/>), each with its text.
/// </summary>
public sealed class PageDescription(string? raw, IReadOnlyList<TextEntry> entries)
{
    public string? Raw { get; } = raw;

    public IReadOnlyList<TextEntry> Entries { get; } = entries;

    /// <summary>The text in <paramref name="language"/>, or null when it has none.</summary>
    public string? TextIn(string language) =>
        Entries.FirstOrDefault(e => string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase))?.Text is { Length: > 0 } text ? text : null;
}
