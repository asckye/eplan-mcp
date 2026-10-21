using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of import.dxf.plan and import.dxf.execute (docs/tools/eplan_import_dxf.md, sections 2 and 5):
/// the page, found as eplan_page_get finds it; the file the Host resolved below its import root (SEC-31), read again
/// for its SHA-256; and how the drawing is scaled and placed. Both operations build it the same way and compute the
/// fingerprint here.
/// </summary>
internal sealed class ImportDxfRequest
{
    /// <summary>The extensions the tool imports (tool spec 2).</summary>
    public static readonly string[] Extensions = [".dxf", ".dwg"];

    private const string Fit = "fit";
    private const string Center = "center";

    private ImportDxfRequest(OpenProject project, MacroTargetPage page, ImportSource source, string scale, string position)
    {
        Project = project;
        Page = page;
        Source = source;
        Scale = scale;
        Position = position;
    }

    public OpenProject Project { get; }

    public MacroTargetPage Page { get; }

    public ImportSource Source { get; }

    /// <summary><c>fit</c> or <c>none</c>.</summary>
    public string Scale { get; }

    /// <summary><c>center</c> or <c>lower_left</c>.</summary>
    public string Position { get; }

    /// <summary>True to scale the drawing to the page (<c>ScalingMode.Automatic</c>).</summary>
    public bool FitToPage => Scale == Fit;

    /// <summary>True for the middle of the page (<c>PositionMode.Center</c>), false for its lower left corner.</summary>
    public bool Centered => Position == Center;

    /// <summary>Every argument problem at once (mcp-tool-design 6.2), then the project, the page and the file.</summary>
    public static ImportDxfRequest Check(ImportDxfArgs args, IProjectCatalog catalog, IPageReader pages, IDrawingImporter importer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (string.IsNullOrEmpty(args.Page))
        {
            violations.Add("'page' is required: a full page name such as =GAA+A1&EFS1/92; eplan_page_create makes a new one.");
        }

        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, Extensions, violations);
        string scale = Given(args.Scale) ?? Fit;
        string position = Given(args.Position) ?? Center;
        if (scale is not (Fit or "none"))
        {
            violations.Add("'scale' must be fit or none.");
        }

        if (position is not (Center or "lower_left"))
        {
            violations.Add("'position' must be center or lower_left.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        MacroTargetPage page = (PageUpdateRequest.Find(pages.PageNames(project.Path), args.Page) is { } name ? importer.ReadPage(project.Path, name) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has this name, so nothing was imported. Use eplan_page_list with name_prefix to find the exact page name, or eplan_page_create to make the page.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = args.Page });

        // At most 100 MB (tool spec 2): read again on the main thread, so a file changed since the Host read it is seen.
        source!.Read(hash: true);
        return new ImportDxfRequest(project, page, source, scale, position);
    }

    /// <summary>The one change, for the dry run, the approval form and the audit (tool spec 5).</summary>
    public PlannedChange Change() =>
        new() { Action = "create", Kind = "drawing", Name = Page.Name, NewValue = $"{Source.Describe()}, scale {Scale}, {Position}" };

    /// <summary>The plan's state (tool spec 5): the objects on the page, and the file's size, last write time and SHA-256.</summary>
    public string StateFingerprint() => Fingerprint.Of(Page.Objects.Select(id => "object\t" + id).Append(Source.FingerprintLine));

    private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
