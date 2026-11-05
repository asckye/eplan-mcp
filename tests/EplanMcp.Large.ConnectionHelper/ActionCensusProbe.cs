using System.Diagnostics;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>ADR-0030 maintainer tool: runs one named action on the census copy, the way the product runs a trial.</summary>
public sealed class ActionCensusProbe : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeActionCensus"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string action = "", result = "", quietMode = "1";
        oActionCallingContext.GetParameter("ACTION", ref action);
        oActionCallingContext.GetParameter("RESULT", ref result);
        oActionCallingContext.GetParameter("QUIET", ref quietMode);
        var row = new JObject { ["action"] = action };
        var watch = Stopwatch.StartNew();
        try
        {
            string project = new SelectionSet().GetCurrentProject(false)?.ProjectFullName ?? "";
            string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            string allowed = Path.Combine(home, "scratch", "writes", "census") + Path.DirectorySeparatorChar;
            if (!project.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                row["refused"] = "The current project is not a census copy.";
            }
            else
            {
                using var quiet = quietMode == "1" ? new QuietModeStep(QuietModes.ShowNoDialogs) : null;
                Eplan.EplApi.ApplicationFramework.Action? found = new ActionManager().FindAction(action);
                row["found"] = found is not null;
                if (found is not null)
                {
                    using var context = new ActionCallingContext();
                    row["returned"] = found.Execute(context);
                }
            }
        }
        catch (Exception error) when (error is BaseException or InvalidOperationException or ArgumentException or NullReferenceException or IOException)
        { row["exception"] = error.GetType().Name + " " + error.Message; }
        row["seconds"] = watch.Elapsed.TotalSeconds;
        File.WriteAllText(result, row.ToString());
        return true;
    }
}
