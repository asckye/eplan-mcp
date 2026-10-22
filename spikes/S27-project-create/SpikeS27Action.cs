using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S27;

/// <summary>
/// Spike S27 (M6 design): creating a project from a basic project (.zw9) and telling a project's database version and
/// compatibility without upgrading it, on the main thread through the dispatcher. Every project it creates or checks
/// is under the scratch root; nothing outside it is written.
/// <para>
/// Usage: <c>EplanMcpSpikeS27 /Mode:createproject|compat /Report:"…\x.json" /Part:"template or .elk" [/Destination:"new .elk"]</c>.
/// </para>
/// </summary>
public sealed class SpikeS27Action : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS27Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S27";
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
            Mode = Parameter(oActionCallingContext, "Mode"),
            Part = Parameter(oActionCallingContext, "Part"),
            Destination = Parameter(oActionCallingContext, "Destination"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S27-project-create", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(600), new Func<bool>(() =>
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
                report["fatal"] = "the dispatcher did not run the probes within 600 s";
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
        switch (p.Mode)
        {
            case "createproject":
                report["create"] = CreateFromBasicProject(p.Part, p.Destination);
                break;
            case "compat":
                report["compat"] = new JArray(p.Part.Split('|').Select(Compatibility));
                break;
            default:
                report["refused"] = "unknown mode " + p.Mode;
                break;
        }
    }

    /// <summary>
    /// <c>ProjectManager.CreateProject(link file, template)</c> (XML: the new project is opened exclusively, API only):
    /// time, the files it creates, whether it is open afterwards, what the new project looks like; then closes it.
    /// </summary>
    private static JObject CreateFromBasicProject(string template, string destination)
    {
        var result = new JObject { ["template"] = template, ["destination"] = destination };
        if (!UnderScratch(destination))
        {
            result["refused"] = "the new project must be under the scratch root";
            return result;
        }

        string folder = Path.GetDirectoryName(destination) ?? string.Empty;
        Directory.CreateDirectory(folder);
        string[] before = Directory.GetFileSystemEntries(folder);
        var clock = Stopwatch.StartNew();
        Project? project = null;
        result["created"] = Try(() =>
        {
            using var manager = new ProjectManager();
            project = manager.CreateProject(destination, template);
            return project is null ? "null" : "project";
        });
        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        result["newEntries"] = new JArray(Directory.GetFileSystemEntries(folder).Except(before).Select(Path.GetFileName));
        if (project is not null)
        {
            Project created = project;
            result["linkFile"] = Try(() => created.ProjectLinkFilePath);
            result["pages"] = Try(() => created.Pages.Length);
            result["isOpen"] = Try(() =>
            {
                using var manager = new ProjectManager();
                return manager.OpenProjects.Any(o => string.Equals(o.ProjectLinkFilePath, created.ProjectLinkFilePath, StringComparison.OrdinalIgnoreCase));
            });
            result["description"] = Try(() => Raw(() => created.Properties.PROJ_DRAWINGNUMBER));
            result["close"] = Try(() =>
            {
                created.Close();
                return "closed";
            });
        }

        result["compatibilityAfter"] = Compatibility(destination);
        return result;
    }

    /// <summary>The database version and compatibility of a project, read without opening or upgrading it (XML docs).</summary>
    private static JObject Compatibility(string path)
    {
        var result = new JObject { ["path"] = path };
        var clock = Stopwatch.StartNew();
        result["compatibility"] = Try(() =>
        {
            using var manager = new ProjectManager();
            return manager.GetProjectDataCompatibility(path).ToString();
        });
        result["databaseVersion"] = Try(() =>
        {
            using var manager = new ProjectManager();
            return manager.GetProjectDatabaseVersion(path).ToString();
        });
        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        result["openAfter"] = Try(() =>
        {
            using var manager = new ProjectManager();
            return manager.OpenProjects.Any(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase));
        });
        return result;
    }

    private static bool UnderScratch(string path) => path.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Raw(Func<PropertyValue> read)
    {
        try
        {
            PropertyValue value = read();
            return value.IsEmpty ? string.Empty : value.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

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

        public string Mode { get; set; } = string.Empty;

        public string Part { get; set; } = string.Empty;

        public string Destination { get; set; } = string.Empty;
    }
}
