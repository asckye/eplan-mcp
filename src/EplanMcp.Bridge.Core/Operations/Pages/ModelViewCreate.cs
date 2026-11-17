using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One checked model view: the action's parameters as InsertModelViewAction takes them (A136, eplan.help).</summary>
public sealed class ModelViewStep
{
    public string Space { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Object ID of the mounting panel or door shown alone (ROOTELEMENTS); null for the whole space.</summary>
    public long? RootId { get; set; }

    public int Viewpoint { get; set; }
    public int Style { get; set; }
    public int ScaleSetting { get; set; }

    /// <summary>The manual scale, e.g. <c>1:5</c>, when <see cref="ScaleSetting"/> is 2 (manually defined); empty otherwise.</summary>
    public string Scale { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>What the adapter read back after inserting: the view's OBJECTID and how many placements the page gained.</summary>
public sealed class ModelViewCreated
{
    public string ObjectId { get; set; } = string.Empty;
    public int Added { get; set; }

    /// <summary>The scale EPLAN shows for the view, e.g. <c>1:5</c>.</summary>
    public string Scale { get; set; } = string.Empty;
}

/// <summary>Inserts a model view, implemented by the EPLAN adapter; main thread, inside the operation's transaction.</summary>
public interface IModelViewWriter
{
    public ModelViewCreated Insert(string projectPath, ModelViewStep view);
}

/// <summary>The checked arguments of eplan_modelview_create, shared by its plan and its execution.</summary>
internal sealed class ModelViewCreateRequest
{
    private static readonly Dictionary<string, int> _viewpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bottom"] = 1,
        ["top"] = 2,
        ["left"] = 3,
        ["right"] = 4,
        ["front"] = 5,
        ["rear"] = 6,
        ["iso_se"] = 7,
        ["iso_sw"] = 8,
        ["iso_ne"] = 9,
        ["iso_nw"] = 10,
    };

    private static readonly Dictionary<string, int> _styles = new(StringComparer.OrdinalIgnoreCase) { ["wireframe"] = 0, ["hidden_lines"] = 1, ["shaded"] = 2 };
    private static readonly Dictionary<string, int> _scales = new(StringComparer.OrdinalIgnoreCase) { ["auto"] = 0, ["fit"] = 1 };
    private static readonly Regex _manualScale = new("^1:([1-9][0-9]{0,2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public OpenProject Project { get; private set; } = new();
    public ModelViewStep Step { get; private set; } = new();
    public string View { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;

    public static ModelViewCreateRequest Check(ModelViewCreateArgs args, IProjectCatalog catalog, ILayoutReader layout, IPageReader pages)
    {
        var violations = new List<string>();
        string space = (args.Space ?? string.Empty).Trim().TrimStart('+');
        string page = (args.Page ?? string.Empty).Trim();
        string view = (args.View ?? "panel").Trim().ToLowerInvariant();
        if (space.Length == 0)
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        if (page.Length == 0)
        { violations.Add("'page' is required: the full name of a ModelView or PanelLayout page."); }
        if (view is not ("panel" or "door" or "cabinet"))
        { violations.Add("'view' must be panel, door or cabinet."); }
        int viewpoint = Pick(_viewpoints, args.Viewpoint, "front", "viewpoint", violations);
        int style = Pick(_styles, args.Style, "hidden_lines", "style", violations);
        string manual = string.Empty;
        int scale;
        if (args.Scale?.Trim() is { } requested && _manualScale.Match(requested) is { Success: true } match)
        {
            scale = 2;
            manual = "1:" + int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            scale = Pick(_scales, args.Scale, "auto", "scale", violations);
        }
        Range(args.X, 0, 2000, "x", violations);
        Range(args.Y, 0, 2000, "y", violations);
        Range(args.Width, 20, 2000, "width", violations);
        Range(args.Height, 20, 2000, "height", violations);
        string name = string.IsNullOrWhiteSpace(args.Name) ? space + " " + view : args.Name!.Trim();
        if (name.Length > 100)
        { violations.Add("'name' must be at most 100 characters."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        LayoutRecord record = layout.Read(project.Path);
        LayoutEnclosure enclosure = record.Spaces.FirstOrDefault(s => string.Equals(s.Designation, space, StringComparison.Ordinal))?.Enclosures.FirstOrDefault()
            ?? throw new BridgeErrorException(ErrorReason.InvalidArgument, "This installation space has no enclosure; place one with eplan_enclosure_place first.", retryable: false,
                new Dictionary<string, string> { ["space"] = space });
        LayoutSurface? surface = view == "panel" ? enclosure.Panel : view == "door" ? enclosure.Door : null;
        if (view != "cabinet" && surface is null)
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, $"The enclosure has no {view}; use view cabinet.", retryable: false,
                new Dictionary<string, string> { ["space"] = space, ["view"] = view });
        }

        PageSummary target = pages.ListPages(project.Path, page, null, null).FirstOrDefault(p => string.Equals(p.Name, page, StringComparison.Ordinal))
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "No page has this full name; create a ModelView page with eplan_page_create first.", retryable: false,
                new Dictionary<string, string> { ["page"] = page });
        if (target.Type is not ("ModelView" or "PanelLayout"))
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, $"The page is a {target.Type} page; a model view goes on a ModelView or PanelLayout page.", retryable: false,
                new Dictionary<string, string> { ["page"] = page, ["pageType"] = target.Type });
        }

        var step = new ModelViewStep
        {
            Space = space,
            Page = page,
            Name = name,
            RootId = surface?.Id,
            Viewpoint = viewpoint,
            Style = style,
            ScaleSetting = scale,
            Scale = manual,
            X = args.X!.Value,
            Y = args.Y!.Value,
            Width = args.Width!.Value,
            Height = args.Height!.Value,
        };
        string state = Fingerprint.Of(record.Objects.Where(o => o.Space == space).Select(o => o.Id.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", o.Box)));
        return new ModelViewCreateRequest { Project = project, Step = step, View = view, State = state };
    }

    public string StateFingerprint() => Fingerprint.Of([Project.Path, Step.Space, Step.Page, Step.Name, View, Step.RootId?.ToString(CultureInfo.InvariantCulture) ?? "",
        string.Join(",", new[] { Step.Viewpoint, Step.Style, Step.ScaleSetting }) + Step.Scale, string.Join(",", new[] { Step.X, Step.Y, Step.Width, Step.Height }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))), State]);

    private static int Pick(Dictionary<string, int> values, string? requested, string fallback, string field, List<string> violations)
    {
        if (values.TryGetValue(string.IsNullOrWhiteSpace(requested) ? fallback : requested!.Trim(), out int value))
        { return value; }
        violations.Add($"'{field}' must be one of {string.Join(", ", values.Keys)}{(field == "scale" ? " or 1:N, N 1 to 999" : "")}.");
        return 0;
    }

    private static void Range(double? value, double min, double max, string field, List<string> violations)
    {
        if (value is not { } v || double.IsNaN(v) || v < min || v > max)
        { violations.Add(FormattableString.Invariant($"'{field}' is required: {min} to {max} page millimetres.")); }
    }
}

/// <summary><see cref="OperationNames.ModelViewCreatePlan"/>: the model view eplan_modelview_create would insert; reads only.</summary>
public sealed class ModelViewCreatePlanOperation(IProjectCatalog catalog, ILayoutReader layout, IPageReader pages) : Operation<ModelViewCreateArgs, WritePlan>
{
    public override string Name => OperationNames.ModelViewCreatePlan;

    protected override WritePlan Execute(ModelViewCreateArgs args)
    {
        ModelViewCreateRequest request = ModelViewCreateRequest.Check(args, catalog, layout, pages);
        ModelViewStep step = request.Step;
        var change = new PlannedChange
        {
            Action = "create",
            Kind = "model view",
            Name = step.Page,
            NewValue = FormattableString.Invariant($"{step.Name}: {request.View} of {step.Space} at ({step.X}, {step.Y}), {step.Width} x {step.Height} mm"),
        };
        return new WritePlan { Project = request.Project.Name, ProjectPath = request.Project.Path, Changes = [change], Fingerprint = request.StateFingerprint() };
    }
}

/// <summary>
/// <see cref="OperationNames.ModelViewCreateExecute"/>: checks again, inserts the view in one transaction and requires
/// that EPLAN returned an OBJECTID and the page gained placements, otherwise the transaction rolls back.
/// </summary>
public sealed class ModelViewCreateExecuteOperation(IProjectCatalog catalog, ILayoutReader layout, IPageReader pages, IModelViewWriter writer, ITransactionScopeFactory transactions)
    : Operation<ModelViewCreateArgs, ModelViewCreateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_modelview_create".</summary>
    public const string Tool = "eplan_modelview_create";

    public override string Name => OperationNames.ModelViewCreateExecute;

    protected override ModelViewCreateData Execute(ModelViewCreateArgs args)
    {
        ModelViewCreateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one modelview.create.plan returned."); }
            request = ModelViewCreateRequest.Check(args, catalog, layout, pages);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The 3D layout changed after the plan was made, so nothing was changed. Call eplan_modelview_create again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Step.Space });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<ModelViewCreated> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            ModelViewCreated created = writer.Insert(request.Project.Path, request.Step);
            return created.ObjectId.Length > 0 && created.Added > 0
                ? created
                : throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                    "EPLAN inserted no model view on the page, so nothing was kept. Check that the installation space has placed objects.",
                    retryable: false, new Dictionary<string, string> { ["page"] = request.Step.Page, ["objectId"] = created.ObjectId });
        });
        return new ModelViewCreateData
        {
            Project = request.Project.Name,
            Page = request.Step.Page,
            Name = request.Step.Name,
            ObjectId = committed.Result.ObjectId,
            Added = committed.Result.Added,
            Scale = committed.Result.Scale,
            Warning = committed.Warning,
        };
    }
}
