namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of the export operations (docs/tools/eplan_export_pdf.md, eplan_export_dxf.md, eplan_export_image.md):
/// each reads the fields its tool has and ignores the others. The plan and the run of one export take the same
/// arguments; only the run has <see cref="Directory"/> and, for PDF, <see cref="FileName"/>.
/// </summary>
public sealed class ExportArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; null means the project selected in EPLAN.</summary>
    public string? Project { get; set; }

    /// <summary>
    /// The pages' full names, found as eplan_page_get finds a page; null means every page of the project (PDF, DXF).
    /// PDF and DXF take 1 to 50, images 1 to 20.
    /// </summary>
    public List<string>? Pages { get; set; }

    /// <summary>PDF, DXF: the name of an export scheme; null means the scheme EPLAN used last.</summary>
    public string? Scheme { get; set; }

    /// <summary>PDF: <c>color</c>, <c>grey</c> or <c>black_white</c>; null means <c>color</c>.</summary>
    public string? Color { get; set; }

    /// <summary>Images: <c>png</c> or <c>jpg</c>; null means <c>png</c>.</summary>
    public string? Format { get; set; }

    /// <summary>Images: the width in pixels, 200 to 4000; null means 1600.</summary>
    public int? WidthPx { get; set; }

    /// <summary>
    /// The run: the full path of the new, empty folder the Host created for this export (SEC-20). EPLAN writes there
    /// and nowhere else. Null for the plan.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>The PDF run: the file's name in <see cref="Directory"/>, extension included, as the Host checked it.</summary>
    public string? FileName { get; set; }
}
