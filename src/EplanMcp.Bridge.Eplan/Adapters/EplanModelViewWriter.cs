using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
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
        int before = EplanNativePreconditions.Valid(page.AllPlacements).Count();

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
        context.AddParameter("SCALESETTING", Number(view.ScaleSetting));
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
        int after = EplanNativePreconditions.Valid(EplanPageDescriptions.Find(projectPath, view.Page).AllPlacements).Count();
        return new ModelViewCreated { ObjectId = id ?? string.Empty, Added = after - before };
    }

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
