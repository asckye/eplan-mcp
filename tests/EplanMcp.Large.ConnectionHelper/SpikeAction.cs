using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Test-only native observations beyond the bounded D1/D2 product planners; never registered by the product.</summary>
public sealed class SpikeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeConnectionSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string requestFile = "";
        oActionCallingContext.GetParameter("REQUEST", ref requestFile);
        var result = new JObject { ["success"] = false };
        try
        {
            // A41/A55: remote callbacks run on the main thread; acquire locking before touching native objects.
            using var locking = new LockingStep();
            JObject request = JObject.Parse(File.ReadAllText(requestFile));
            Project project = new SelectionSet().GetCurrentProject(false);
            string root = Environment.GetEnvironmentVariable("EPLANMCP_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            string expected = Path.Combine(root, "scratch", "writes", "LargeWrite");
            if (!string.Equals(project.ProjectFullName, expected, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)request["project"], StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Only the current LargeWrite scratch copy is allowed."); }
            string[] names = request["pages"]!.Values<string>().Select(p => p!).ToArray();
            if (names.Length is < 1 or > 2 || names.Distinct(StringComparer.Ordinal).Count() != names.Length
                || names.Any(p => !p.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal)))
            { throw new InvalidOperationException("Supply one or two explicit new Circuit pages."); }
            Page[] pages = names.Select(n => project.Pages.Single(p => p.Name == n && p.PageType.ToString() == "Circuit")).ToArray();
            result["mode"] = request["mode"]!.DeepClone();
            if ((string?)request["mode"] != "read")
            {
                // Native wrappers never escape a stage. On Abort, do not inspect possibly invalid new handles (A55).
                project.LockAllObjects();
                using var manager = new UndoManager();
                UndoStep undo = manager.CreateUndoStep();
                undo.SetUndoDescription("Connection D1/D2 spike");
                using Transaction transaction = new TransactionManager().CreateTransaction();
                try
                {
                    using (var quiet = new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        switch ((string?)request["mode"])
                        {
                            case "define":
                                SpikeCable.Define(project, pages[0], request);
                                break;
                            case "bind":
                                SpikeCable.Bind(project, pages[0], request);
                                break;
                            case "templates":
                                SpikeCable.Templates(project, pages, request);
                                break;
                            case "assign":
                                SpikeCable.Assign(pages[0], request);
                                break;
                            case "rename":
                                Rename(project, pages, request);
                                break;
                            default:
                                throw new InvalidOperationException("Unknown connection spike mode.");
                        }
                    }
                    transaction.Commit();
                    undo.CloseUndo();
                }
                catch { transaction.Abort(); throw; }
            }
            result["success"] = true;
            // A55: fresh page/finder lookup after disposing the committed Transaction, never retained created objects.
            result["observation"] = SpikeReadback.Read(project, names);
        }
        catch (Exception ex)
        {
            result["error"] = ex.ToString();
        }
        // Remote Client success alone is not evidence (A1); the caller reads this unique response file.
        File.WriteAllText(requestFile + ".result.json", result.ToString());
        // A reported native refusal is a diagnostic result, not a failed transport/setup assertion in ActionAsync.
        return true;
    }

    private static void Rename(Project project, Page[] pages, JObject request)
    {
        string name = (string)request["name"]!;
        long[] ids = request["oids"]!.Values<long>().ToArray();
        InterruptionPoint[] points = pages.SelectMany(p => p.AllPlacements.OfType<InterruptionPoint>()).Where(p => ids.Contains(p.ObjectIdentifier)).ToArray();
        using var finder = new DMObjectsFinder(project);
        if (points.Length != ids.Length || finder.GetInterruptionPoints(null).Any(p => p.Name == name && !pages.Any(page => page.Name == p.Page.Name)))
        { throw new InvalidOperationException("Rename must stay within the new pages and an isolated business name."); }
        foreach (InterruptionPoint point in points)
        { point.Name = name; }
    }
}
