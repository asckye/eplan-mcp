namespace EplanMcp.Contracts;

/// <summary>Read-only page geometry (A3/A14/A52/A54); unknown geometry is not an unbounded drawing area.</summary>
public sealed class PageDrawingGeometry
{
    /// <summary>The resolved project path; used to bind drawing discovery to one project.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>The exact full page name.</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>The page's grid spacing in millimetres; 1 API unit = 1 mm (A3).</summary>
    public double GridSize { get; set; }
    /// <summary>Page.Size width/height in millimetres, or null when unreadable, including empty pages (A14).</summary>
    public double[]? PageSize { get; set; }
    /// <summary>PlotFrame.Size width/height as read, never a fallback drawing boundary: S42 did not verify its origin/scale.</summary>
    public double[]? PlotFrameSize { get; set; }
    /// <summary>Supported placements and property children; excludes plot-frame descendants and part references.</summary>
    public List<DrawingPlacement> Placements { get; set; } = [];

    /// <summary>Serializer-independent page evidence for approval rechecks, including grid and observed bounds.</summary>
    public string Fingerprint()
    {
        string value = string.Join("|", new[] { GridSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        string.Join(",", (PageSize ?? []).Select(n => n.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) }
            .Concat(Placements.Select(p => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1}:{2}", p.Locator.Type, p.Locator.Oid, p.Locator.Digest)).OrderBy(d => d, StringComparer.Ordinal)));
        using var hash = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
