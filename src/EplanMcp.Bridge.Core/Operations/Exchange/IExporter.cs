namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What an export makes (docs/tools/eplan_export_pdf.md, eplan_export_dxf.md, eplan_export_image.md).</summary>
public enum ExportKind
{
    Pdf,
    Dxf,
    Image,
}

/// <summary>The colours of a PDF: EPLAN's <c>Export.DegreeOfColor</c> (EPLAN XML docs).</summary>
public enum ExportColor
{
    Color,
    Grey,
    BlackWhite,
}

/// <summary>The image formats eplan_export_image offers.</summary>
public enum ImageFormat
{
    Png,
    Jpg,
}

/// <summary>
/// Exports pages and reads EPLAN's export schemes, implemented by the EPLAN adapter layer (the tool specs, section 5;
/// api-notes A14, A16). Main thread only. Page names are exact, as the operation resolved them in the same call; folders
/// and files are full paths in the folder the Host created. A failed export throws EPLAN_OPERATION_FAILED.
/// </summary>
public interface IExporter
{
    /// <summary>Whether EPLAN has a <paramref name="kind"/> scheme (PDF or DXF) of this name.</summary>
    public bool SchemeExists(ExportKind kind, string scheme);

    /// <summary>The names of the <paramref name="kind"/> schemes, in EPLAN's order; reading them leaves the last used one as it is.</summary>
    public IReadOnlyList<string> Schemes(ExportKind kind);

    /// <summary>The <paramref name="kind"/> scheme EPLAN used last, which an export without a scheme name uses (A16).</summary>
    public string LastUsedScheme(ExportKind kind);

    /// <summary>Makes <paramref name="scheme"/> the last used <paramref name="kind"/> scheme again; false when EPLAN refuses.</summary>
    public bool TrySetLastUsedScheme(ExportKind kind, string scheme);

    /// <summary>Exports the pages as one PDF named <paramref name="file"/>; an empty <paramref name="scheme"/> means the last used one.</summary>
    public void ExportPdf(string projectPath, IReadOnlyList<string> pages, string scheme, ExportColor color, string file);

    /// <summary>
    /// Exports the pages as DXF or DWG, one file per page, into <paramref name="directory"/>, where the scheme may add
    /// subfolders (A16); an empty <paramref name="scheme"/> means the last used one.
    /// </summary>
    public void ExportDxf(string projectPath, IReadOnlyList<string> pages, string scheme, string directory);

    /// <summary>Exports one page as an image <paramref name="widthPx"/> pixels wide into <paramref name="directory"/> (A14).</summary>
    public void ExportImage(string projectPath, string page, string directory, ImageFormat format, int widthPx);
}
