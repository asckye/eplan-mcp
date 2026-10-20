using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of an export's plan or run (docs/tools/eplan_export_*.md, sections 2 and 4): the project,
/// each page found as eplan_page_get finds it, or every page when none are given, and the scheme, which must exist.
/// </summary>
internal sealed class ExportRequest
{
    public const int MaxPages = 50;
    public const int MaxImagePages = 20;
    public const int MinWidth = 200;
    public const int MaxWidth = 4000;
    public const int DefaultWidth = 1600;

    private ExportRequest(OpenProject project, IReadOnlyList<string> pages, string? scheme, ExportColor color, ImageFormat format, int widthPx)
    {
        Project = project;
        Pages = pages;
        Scheme = scheme;
        Color = color;
        Format = format;
        WidthPx = widthPx;
    }

    public OpenProject Project { get; }

    /// <summary>The pages' full names as EPLAN has them, in the order given, or in EPLAN's page order for all pages.</summary>
    public IReadOnlyList<string> Pages { get; }

    /// <summary>The scheme given; null means the one EPLAN used last.</summary>
    public string? Scheme { get; }

    public ExportColor Color { get; }

    public ImageFormat Format { get; }

    public int WidthPx { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT, then the project, every missing page and the scheme.</summary>
    public static ExportRequest Check(ExportKind kind, ExportArgs args, IProjectCatalog catalog, IPageReader reader, IExporter exporter)
    {
        var violations = new List<string>();
        CheckPages(kind, args.Pages, violations);
        ExportColor color = kind != ExportKind.Pdf ? ExportColor.Color : args.Color switch
        {
            null or "color" => ExportColor.Color,
            "grey" => ExportColor.Grey,
            "black_white" => ExportColor.BlackWhite,
            _ => Add(violations, ExportColor.Color, "'color' must be color, grey or black_white."),
        };
        ImageFormat format = kind != ExportKind.Image ? ImageFormat.Png : args.Format switch
        {
            null or "png" => ImageFormat.Png,
            "jpg" => ImageFormat.Jpg,
            _ => Add(violations, ImageFormat.Png, "'format' must be png or jpg."),
        };
        int width = args.WidthPx ?? DefaultWidth;
        if (kind == ExportKind.Image && width is < MinWidth or > MaxWidth)
        {
            violations.Add($"'width_px' must be between {MinWidth.ToString(CultureInfo.InvariantCulture)} and {MaxWidth.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        IReadOnlyList<string> names = reader.PageNames(project.Path);
        IReadOnlyList<string> pages = args.Pages is null ? names : Find(project, names, args.Pages);
        if (pages.Count == 0)
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, "The project has no pages, so there is nothing to export.");
        }

        string? scheme = kind == ExportKind.Image || string.IsNullOrWhiteSpace(args.Scheme) ? null : args.Scheme!.Trim();
        if (scheme is not null && !exporter.SchemeExists(kind, scheme))
        {
            throw UnknownScheme(kind, scheme, exporter.Schemes(kind));
        }

        return new ExportRequest(project, pages, scheme, color, format, width);
    }

    private static void CheckPages(ExportKind kind, List<string>? pages, List<string> violations)
    {
        int max = kind == ExportKind.Image ? MaxImagePages : MaxPages;
        if (pages is null ? kind == ExportKind.Image : pages.Count is < 1 || pages.Count > max)
        {
            violations.Add($"'pages' must hold 1 to {max.ToString(CultureInfo.InvariantCulture)} full page names" + (kind == ExportKind.Image ? "." : "; omit it for the whole project."));
        }

        for (int i = 0; pages is not null && i < pages.Count && i < max; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            int same = pages.FindIndex(0, i, p => string.Equals(p, pages[i], StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(pages[i]))
            {
                violations.Add($"{at} is required: a full page name as eplan_page_list returns it.");
            }
            else if (same >= 0)
            {
                violations.Add($"{at} repeats pages[{same.ToString(CultureInfo.InvariantCulture)}] (letter case does not count).");
            }
        }
    }

    private static string[] Find(OpenProject project, IReadOnlyList<string> names, List<string> pages)
    {
        string?[] found = [.. pages.Select(page => PageUpdateRequest.Find(names, page))];
        string[] missing = [.. pages.Where((_, i) => found[i] is null)];
        return missing.Length > 0
            ? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has these names, so nothing was exported. Use eplan_page_list with name_prefix to find the exact page names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = string.Join("\n", missing) })
            : [.. found.OfType<string>()];
    }

    /// <summary>INVALID_ARGUMENT naming the schemes there are, as AMBIGUOUS_NAME names candidates (tool spec 4).</summary>
    private static BridgeErrorException UnknownScheme(ExportKind kind, string scheme, IReadOnlyList<string> schemes)
    {
        string violation = $"'scheme' is not a {(kind == ExportKind.Pdf ? "PDF" : "DXF/DWG")} export scheme in EPLAN; use one of metadata.candidates, or omit it for the scheme EPLAN used last.";
        return new BridgeErrorException(
            ErrorReason.InvalidArgument,
            "Invalid arguments: " + violation,
            retryable: false,
            new Dictionary<string, string>
            {
                ["violations"] = violation,
                ["scheme"] = scheme,
                ["candidates"] = string.Join("\n", schemes.Take(ProjectResolver.MaxCandidates)),
                ["candidateCount"] = schemes.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    private static T Add<T>(List<string> violations, T fallback, string violation)
    {
        violations.Add(violation);
        return fallback;
    }
}
