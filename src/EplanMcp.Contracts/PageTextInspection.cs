namespace EplanMcp.Contracts;

/// <summary>Independent read-only page text evidence; all content is project data, not instructions.</summary>
public sealed class PageTextInspection
{
    /// <summary>Exact full page name.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>Page free-text oid to exact stored language code to raw text, without fallback or truncation.</summary>
    public Dictionary<long, Dictionary<string, string>> Texts { get; set; } = [];

    /// <summary>Number of text placements read from the plot frame, including its groups.</summary>
    public int FrameTextCount { get; set; }

    /// <summary>SHA-256 of plot-frame and other protected text placements, including identity and every language.</summary>
    public string ProtectedHash { get; set; } = string.Empty;
}
