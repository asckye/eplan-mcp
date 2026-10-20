using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The run of one export tool (<see cref="OperationNames.ExportPdfRun"/>, <see cref="OperationNames.ExportDxfRun"/>,
/// <see cref="OperationNames.ExportImageRun"/>; docs/tools/eplan_export_*.md, section 5): checks the arguments again and
/// that the Host's folder is there and empty, then exports into that folder only (SEC-20). PDF and DXF are one EPLAN
/// call each, which cannot be interrupted, so a cancel is reported when it has returned (M4 design 4.3); images go page
/// by page, with progress and a safe point before each. A read: the project's design data does not change (A16).
/// </summary>
public sealed class ExportRunOperation(ExportKind kind, IProjectCatalog catalog, IPageReader reader, IExporter exporter) : Operation<ExportArgs, ExportRunData>
{
    public override string Name => kind switch
    {
        ExportKind.Pdf => OperationNames.ExportPdfRun,
        ExportKind.Dxf => OperationNames.ExportDxfRun,
        _ => OperationNames.ExportImageRun,
    };

    protected override ExportRunData Execute(ExportArgs args)
    {
        var request = ExportRequest.Check(kind, args, catalog, reader, exporter);
        string file = kind == ExportKind.Pdf ? PdfFileName(args.FileName) : string.Empty;
        string directory = ExportFolderCheck.CheckEmpty(args.Directory);
        var data = new ExportRunData { Project = request.Project.Name, PageCount = request.Pages.Count };
        if (kind == ExportKind.Image)
        {
            data.Files = Images(request, directory);
            return data;
        }

        // Always the Host's folder, never the scheme's (SEC-20); an empty scheme name is the last used one (A16).
        string before = exporter.LastUsedScheme(kind);
        bool setBack;
        try
        {
            if (kind == ExportKind.Pdf)
            {
                exporter.ExportPdf(request.Project.Path, request.Pages, request.Scheme ?? string.Empty, request.Color, Path.Combine(directory, file));
            }
            else
            {
                exporter.ExportDxf(request.Project.Path, request.Pages, request.Scheme ?? string.Empty, directory);
            }
        }
        finally
        {
            // Whether a named scheme becomes the last used one is not verified (tool spec 5); if so, it goes back.
            setBack = exporter.LastUsedScheme(kind) == before || exporter.TrySetLastUsedScheme(kind, before);
        }

        OperationContext.Current.ThrowIfCancellationRequested(); // the safe point after the EPLAN call
        data.Scheme = request.Scheme ?? before;
        data.Warning = setBack
            ? null
            : $"The export changed the {(kind == ExportKind.Pdf ? "PDF" : "DXF/DWG")} scheme EPLAN offers first next time, and it could not be set back to '{before}'; the engineer can choose it again in EPLAN's export dialog.";
        return data;
    }

    /// <summary>The PDF's name in the folder, as the Host checked it; checked again so that EPLAN never gets a path (SEC-20).</summary>
    private static string PdfFileName(string? fileName)
    {
        string name = fileName ?? string.Empty;
        return name.Length > 0 && name.IndexOfAny(['\\', '/', ':']) < 0 && name.IndexOf("..", StringComparison.Ordinal) < 0 && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? name
            : throw InvalidArgument("'fileName' must be a file name ending in .pdf, without a folder.");
    }

    /// <summary>
    /// One page at a time: the file a page became is the one file that appeared in the folder meanwhile, whatever EPLAN
    /// named it (tool spec 5). A page that added none, or more than one, fails the export.
    /// </summary>
    private List<ExportedPage> Images(ExportRequest request, string directory)
    {
        OperationContext context = OperationContext.Current;
        int count = request.Pages.Count;
        var files = new List<ExportedPage>();
        HashSet<string> before = ExportFolderCheck.Files(directory);
        for (int i = 0; i < count; i++)
        {
            context.ReportProgress(i, count, string.Format(CultureInfo.InvariantCulture, "Exporting page {0} of {1}", i + 1, count));
            context.ThrowIfCancellationRequested();
            exporter.ExportImage(request.Project.Path, request.Pages[i], directory, request.Format, request.WidthPx);
            HashSet<string> after = ExportFolderCheck.Files(directory);
            string[] added = [.. after.Where(path => !before.Contains(path))];
            if (added.Length != 1)
            {
                throw new BridgeErrorException(
                    ErrorReason.EplanOperationFailed,
                    "EPLAN did not write exactly one new image for a page, for example because two pages gave the same file name, so the export failed and no files were kept. Export that page on its own.",
                    retryable: false,
                    new Dictionary<string, string> { ["page"] = request.Pages[i], ["newFiles"] = added.Length.ToString(CultureInfo.InvariantCulture) });
            }

            files.Add(new ExportedPage { Page = request.Pages[i], Path = added[0] });
            before = after;
        }

        context.ReportProgress(count, count, string.Format(CultureInfo.InvariantCulture, "Exported {0} of {0} pages", count));
        context.ThrowIfCancellationRequested(); // the safe point after the last EPLAN call
        return files;
    }
}
