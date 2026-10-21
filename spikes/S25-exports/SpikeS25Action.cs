using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S25;

/// <summary>
/// Spike S25 (M4c design 4.9): the exports of the first M4c batch, run the way the Bridge runs an export (main thread
/// through the dispatcher, after the scheduling action has returned, in a QuietModeStep, no SafetyPoint) as S11 did for
/// PDF and DXF: PXF, parts list, device list, labels, PLC address overview, missing-word list. Each writes into its own
/// empty folder; the driver lists what appeared there and what changed in the project.
/// <para>
/// Usage: <c>EplanMcpSpikeS25 /Mode:projects|select|restore|pxf|pxfnomaster|partsxml|partscsv|devicescsv|labeltext|labelexcel|plcaddresses|missingwords|lastused|close
/// /Report:"…\x.json" /ScratchRoot:"…\s25" /Out:"…\dir" [/Restore:name]</c>. Works only on an open project under
/// ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS25Action : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS25Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S25";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var p = new Params
        {
            Report = Parameter(oActionCallingContext, "Report"),
            ScratchRoot = Parameter(oActionCallingContext, "ScratchRoot"),
            Mode = Parameter(oActionCallingContext, "Mode"),
            Out = Parameter(oActionCallingContext, "Out"),
            Restore = Parameter(oActionCallingContext, "Restore"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S25-exports", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(900), new Func<bool>(() =>
            {
                // Everything is caught in here: an exception escaping a dispatched delegate ends EPLAN (S1).
                try
                {
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        Run(report, p);
                    }
                }
                catch (Exception ex)
                {
                    report["fatal"] = Describe(ex);
                }

                return true;
            }));
            if (done is null)
            {
                report["fatal"] = "the dispatcher did not run the probes within 900 s";
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }

        File.WriteAllText(p.Report + ".partial", report.ToString(Formatting.Indented));
        File.Delete(p.Report);
        File.Move(p.Report + ".partial", p.Report);
    }

    private static void Run(JObject report, Params p)
    {
        Project[] open = OpenProjects();
        report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
        if (p.Mode == "projects")
        {
            report["open"] = new JArray(open.Select(o => o.ProjectName));
            return;
        }

        if (p.Mode == "restore")
        {
            Project? previous = open.FirstOrDefault(o => o.ProjectName == p.Restore);
            report["restored"] = previous is not null && new Edit().SelectProjectInPagesNavigator(previous);
            return;
        }

        if (p.Mode == "lastused")
        {
            report["lastUsed"] = new JObject
            {
                ["labelling"] = LastUsed("USER.Labelling.Config"),
                ["pxfSchemes"] = LastUsed("USER.MacrosGui.PXF.Export.Schemes"),
            };
            return;
        }

        Project? project = open.FirstOrDefault(o => IsUnder(o.ProjectFullName, p.ScratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        string Out(string file) => Path.Combine(p.Out, file);
        switch (p.Mode)
        {
            case "select":
                report["select"] = new Edit().SelectProjectInPagesNavigator(project);
                break;
            case "pxf":
                report["export"] = Timed(() => new Export().PXFProject(project, Out("s25"), true, true));
                break;
            case "pxfnomaster":
                report["export"] = Timed(() => new Export().PXFProject(project, Out("s25-nomaster"), false, true));
                break;
            case "partsxml":
                report["export"] = Timed(() => new PartsService().ExportPartsList(project, Out("parts.xml"), PartsService.Format.XML));
                break;
            case "partscsv":
                report["export"] = Timed(() => new PartsService().ExportPartsList(project, Out("parts.csv"), PartsService.Format.CSV));
                break;
            case "devicescsv":
                report["export"] = Timed(() => new DeviceService().ExportDeviceList(project, Out("devices.csv"), DeviceService.Format.CSV));
                break;
            case "labeltext":
                report["export"] = Timed(() => new Label().DoLabel(project, "Device tag list", "en_US", Out("labels.txt"), 1, 1));
                break;
            case "labelexcel":
                report["export"] = Timed(() => new Label().DoLabel(project, "Terminal diagram", "en_US", Out("terminals.xlsx"), 1, 1));
                break;
            case "plcaddresses":
                report["export"] = Timed(() => new PlcService().ExportAddressOverview(project, string.Empty, string.Empty, string.Empty, Out("plc-addresses.csv")));
                break;
            case "missingwords":
                report["projectLanguages"] = Try(() => Languages(project));
                report["export"] = Timed(() =>
                {
                    var languages = new StringCollection { "en_US" };
                    new Translate().ExportMissingTranslation(Out("missing.txt"), "XTrLanguageDbXml2TabConverterImpl", languages, [project]);
                });
                break;
            case "close":
                project.Close();
                report["close"] = "closed";
                break;
            default:
                report["refused"] = "unknown mode " + p.Mode;
                break;
        }
    }

    private static JToken Languages(Project project)
    {
        var settings = new ProjectSettings(project);
        return new JObject
        {
            ["translation"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.TRANSLATE_LANGUAGES", 0)),
            ["display"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.DISPLAYED_LANGUAGES", 0)),
            ["source"] = Try(() => settings.GetStringSetting("TRANSLATEGUI.SOURCE_LANGUAGE", 0)),
        };
    }

    private static JToken Timed(System.Action work)
    {
        var clock = Stopwatch.StartNew();
        var result = new JObject();
        try
        {
            work();
            result["ok"] = true;
        }
        catch (Exception ex)
        {
            result["ok"] = false;
            result["error"] = Describe(ex);
        }

        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        return result;
    }

    private static JToken LastUsed(string node) => Try(() =>
    {
        using var scheme = new SchemeSetting();
        scheme.Init(node);
        return scheme.GetLastUsed();
    });

    private static Project[] OpenProjects()
    {
        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            return manager.OpenProjects;
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }

    private static bool IsUnder(string? path, string root) =>
        path is not null && root.Length > 0
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static JToken Try(Func<JToken?> probe)
    {
        try
        {
            return probe() ?? JValue.CreateNull();
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static JObject Describe(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex.InnerException is not null)
        {
            o["inner"] = Describe(ex.InnerException);
        }

        return o;
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }

    private sealed class Params
    {
        public string Report { get; set; } = string.Empty;

        public string ScratchRoot { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public string Out { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;
    }
}
