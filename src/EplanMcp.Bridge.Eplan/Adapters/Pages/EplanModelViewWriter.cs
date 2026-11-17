using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Inserts a model view with InsertModelViewAction through the command line interpreter; ActionManager's Execute returns
/// false for it (A136, live 2026-11-12). LAYOUTSPACE is the 36452 value. ROOTELEMENTS takes FUNCTION3D_ID_RELATIVE (36035) values separated by
/// '#' (eplan.help). Main thread, inside the operation's transaction.
/// </summary>
internal sealed class EplanModelViewWriter : IModelViewWriter
{
    public ModelViewCreated Insert(string projectPath, ModelViewStep view)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        Page page = EplanPageDescriptions.Find(projectPath, view.Page);
        Placement[] existing = [.. EplanNativePreconditions.Valid(page.AllPlacements)];

        using var context = new ActionCallingContext();
        context.AddParameter("PROJECTNAME", project.ProjectLinkFilePath);
        context.AddParameter("PAGENAME", view.Page);
        context.AddParameter("LAYOUTSPACE", view.Space);
        context.AddParameter("VIEWNAME", view.Name);
        context.AddParameter("X", Number(view.X));
        context.AddParameter("Y", Number(view.Y));
        context.AddParameter("DX", Number(view.Width));
        context.AddParameter("DY", Number(view.Height));
        context.AddParameter("VIEWPOINT", Number(view.Viewpoint));
        context.AddParameter("STYLE", Number(view.Style));
        // SCALESETTING and SCALE are ignored when the action runs from the add-in (every view read back 1:10, A136);
        // the scale is set on the ViewPlacement below.
        context.AddParameter("VIEWTYPE", "1");
        if (view.RootId is { } root)
        {
            context.AddParameter("ROOTELEMENTS", RelativeId(project, root));
        }

#pragma warning disable RS0030 // InsertModelViewAction only, with checked parameters, inside the operation's TransactionScope (tool spec 4).
        // ActionManager's Execute returns false for this action and the command line interpreter runs it, but only after
        // ActionManager has executed it once in this EPLAN session (A136, live 2026-11-12); so both, in this order.
        global::Eplan.EplApi.ApplicationFramework.Action action = new ActionManager().FindAction("InsertModelViewAction")
            ?? throw new InvalidOperationException("EPLAN has no 'InsertModelViewAction' action.");
        if (!action.Execute(context) && !new CommandLineInterpreter().Execute("InsertModelViewAction", context))
#pragma warning restore RS0030
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused to insert the model view; nothing was kept. Check the installation space and the page.",
                retryable: false, new Dictionary<string, string> { ["page"] = view.Page, ["space"] = view.Space });
        }

        string id = string.Empty;
        context.GetParameter("OBJECTID", ref id);
        Placement[] now = [.. EplanNativePreconditions.Valid(EplanPageDescriptions.Find(projectPath, view.Page).AllPlacements)];
        // The view is not among Page.AllPlacements; OBJECTID is "<db>/190/<oid>/0", and the finder lists it (live 2026-11-12).
        string[] parts = (id ?? string.Empty).Split('/');
        ViewPlacement? placed = null;
        if (parts.Length > 2 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long oid))
        {
            using var finder = new DMObjectsFinder(project);
            placed = EplanNativePreconditions.Valid(finder.GetPlacements(null)).OfType<ViewPlacement>().FirstOrDefault(p => p.ObjectIdentifier == oid);
        }

        string scale = placed is null
            ? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The inserted model view could not be found by its OBJECTID; nothing was kept.", retryable: false,
                new Dictionary<string, string> { ["objectId"] = id ?? string.Empty })
            : SetScale(placed, view);
        return new ModelViewCreated { ObjectId = id ?? string.Empty, Added = now.Length - existing.Length, Scale = scale };
    }

    /// <summary>
    /// Sets fit or a manual 1:N scale through the API and returns the scale EPLAN shows (36510, e.g. "1:10"). Whether
    /// <see cref="ViewPlacement.Scale"/> takes 1/N or N is not documented; 1/N is tried first and checked against 36510.
    /// </summary>
    private static string SetScale(ViewPlacement placed, ModelViewStep view)
    {
        if (view.ScaleSetting == 1)
        {
            placed.ViewScale = ViewScaleType.ScaleFit;
            placed.Update();
        }
        else if (view.ScaleSetting == 2)
        {
            double n = double.Parse(view.Scale.Substring(2), CultureInfo.InvariantCulture);
            placed.ViewScale = ViewScaleType.ScaleManual;
            placed.Scale = 1 / n;
            placed.Update();
            if (Shown(placed) != view.Scale)
            {
                placed.Scale = n;
                placed.Update();
            }

            if (Shown(placed) != view.Scale)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                    $"EPLAN shows the view at {Shown(placed)}, not {view.Scale}; nothing was kept.", retryable: false);
            }
        }

        return Shown(placed);
    }

    private static string Shown(ViewPlacement placed) => placed.Properties[36510].ToString() ?? string.Empty;

    private static string RelativeId(Project project, long objectId)
    {
        using var finder = new DMObjectsFinder(project);
        Function3D root = EplanNativePreconditions.Valid(finder.GetFunctions3D(null)).FirstOrDefault(f => f.ObjectIdentifier == objectId)
            ?? throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The mounting panel or door is no longer in the layout; read eplan_layout_get again.", retryable: false);
        string value = root.Properties[36035].ToString();
        return value.Length > 0 ? value : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The mounting panel or door has no FUNCTION3D_ID_RELATIVE; use view cabinet.", retryable: false);
    }

    private static string Number(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);
}
