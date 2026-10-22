using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EplanEventHandler = Eplan.EplApi.ApplicationFramework.EventHandler;

namespace EplanMcp.Spikes.S33;

/// <summary>
/// Spike S33 (ADR-0026): which EPLAN events fire, with which parameters, on which thread and how often. <c>listen</c>
/// registers <see cref="EplanEventHandler"/> objects for a list of candidate names (kept alive in a static list);
/// <c>operate</c> changes a project under the scratch root (pages created and rolled back, created, renamed in their
/// description, deleted, the project closed and reopened); <c>collect</c> reports what was recorded and disposes the
/// handlers. The candidate names are guesses to be confirmed: a name nothing raises simply records nothing.
/// <para>Usage: <c>EplanMcpSpikeS33 /Mode:listen|operate|collect /Report:"…\x.json" /Project:"…\x.elk"</c>.</para>
/// </summary>
public sealed class SpikeS33Action : IEplAction
{
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";

    /// <summary>Candidate event names: wildcards and names reported for other EPLAN versions, none verified for 2026.</summary>
    private static readonly string[] _candidates =
    [
        "*",
        "onActionStart.String.*",
        "onActionEnd.String.*",
        "Eplan.EplApi.OnPostOpenProject",
        "Eplan.EplApi.OnPreCloseProject",
        "Eplan.EplApi.OnUserPreCloseProject",
        "Eplan.EplApi.OnMainEnd",
        "Eplan.EplApi.OnDataChanged",
        "NotifyPageOpened",
        "NotifyPageClosed",
        "NotifyPageChanged",
        "Page.ConnectionDirty",
        "PageManagement.ProjectAlreadyOpen",
    ];

    private static readonly List<EplanEventHandler> _handlers = [];
    private static readonly ConcurrentQueue<Recorded> _events = new();
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static int _mainThread;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS33Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S33";
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
            Project = Parameter(oActionCallingContext, "Project"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S33-events", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(600), new Func<bool>(() =>
            {
                try
                {
                    report[p.Mode] = p.Mode switch
                    {
                        "listen" => Listen(),
                        "operate" => Operate(p.Project),
                        "collect" => Collect(),
                        _ => "unknown mode",
                    };
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

    private static JObject Listen()
    {
        _mainThread = Environment.CurrentManagedThreadId;
        var registered = new JArray();
        foreach (string candidate in _candidates)
        {
            try
            {
                var handler = new EplanEventHandler(candidate);
                string listenedFor = candidate;
                handler.EplanNameEvent += (parameter, name) => Record(listenedFor, name, parameter);
                _handlers.Add(handler);
                registered.Add(candidate);
            }
            catch (Exception ex)
            {
                registered.Add(candidate + ": " + ex.GetType().Name + " " + ex.Message);
            }
        }

        return new JObject { ["mainThread"] = _mainThread, ["registered"] = registered };
    }

    private static void Record(string listenedFor, string name, IEventParameter parameter)
    {
        string text;
        try
        {
            using var value = new EventParameterString(parameter);
            text = value.String ?? string.Empty;
        }
        catch (Exception ex)
        {
            text = "!" + ex.GetType().Name;
        }

        _events.Enqueue(new Recorded(listenedFor, name, text, Environment.CurrentManagedThreadId, _clock.Elapsed.TotalMilliseconds, Marker));
    }

    private static string Marker { get; set; } = "before";

    /// <summary>Changes the scratch project step by step, marking which step each recorded event belongs to.</summary>
    private static JObject Operate(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("operate works only under " + _scratchRoot);
        }

        var steps = new JObject();
        using var locking = new LockingStep(); // r2: OpenProjects needs it too (S063110 in r1)
        Project project = Open(path);
        {
            Step(steps, "create rolled back", () =>
            {
                using SafetyPoint safety = SafetyPoint.Create();
                NewPage(project, 961);
                safety.Rollback();
            });
            long created = 0;
            Step(steps, "create committed", () =>
            {
                using SafetyPoint safety = SafetyPoint.Create();
                created = NewPage(project, 962).ObjectIdentifier;
                safety.Commit();
            });
            Step(steps, "describe committed", () =>
            {
                using SafetyPoint safety = SafetyPoint.Create();
                using var text = new MultiLangString();
                text.AddString(ISOCode.Language.L___, "S33");
                Find(project, created).Properties.PAGE_NOMINATIOMN = text;
                safety.Commit();
            });
            Step(steps, "delete committed", () =>
            {
                using SafetyPoint safety = SafetyPoint.Create();
                Find(project, created).Remove();
                safety.Commit();
            });
        }

        Step(steps, "close and reopen", () =>
        {
            Open(path).Close();
            using var manager = new ProjectManager();
            manager.OpenProject(path);
        });
        Marker = "after operate";
        return steps;
    }

    private static void Step(JObject steps, string name, System.Action action)
    {
        Marker = name;
        int before = _events.Count;
        var clock = Stopwatch.StartNew();
        try
        {
            action();
            steps[name] = new JObject { ["ms"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1), ["events"] = _events.Count - before };
        }
        catch (Exception ex)
        {
            steps[name] = new JObject { ["error"] = Describe(ex) };
        }
    }

    private static JObject Collect()
    {
        Recorded[] all = _events.ToArray();
        var byName = all.GroupBy(e => e.ListenedFor + " -> " + e.Name).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new JObject
        {
            ["handler"] = g.Key,
            ["count"] = g.Count(),
            ["steps"] = string.Join(", ", g.Select(e => e.Step).Distinct()),
            ["threads"] = string.Join(",", g.Select(e => e.Thread).Distinct()),
            ["parameters"] = new JArray(g.Select(e => e.Parameter).Distinct().Take(5)),
        });
        foreach (EplanEventHandler handler in _handlers)
        {
            handler.Dispose();
        }

        _handlers.Clear();
        return new JObject
        {
            ["mainThread"] = _mainThread,
            ["total"] = all.Length,
            ["byHandlerAndName"] = new JArray(byName.Take(150)),
            ["firstSequence"] = new JArray(all.Take(60).Select(e => Math.Round(e.Ms, 1).ToString(CultureInfo.InvariantCulture) + " " + e.Step + " | " + e.ListenedFor + " -> " + e.Name + " | " + Cut(e.Parameter))),
        };
    }

    private static string Cut(string s) => s.Length <= 160 ? s : s.Substring(0, 160);

    private static Project Open(string path)
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("not open: " + path);
    }

    private static Page NewPage(Project project, int counter)
    {
        using var finder = new DMObjectsFinder(project);
        Page template = finder.GetPages(null).First(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit);
        using var parts = new PagePropertyList();
        parts.DESIGNATION_FUNCTIONALASSIGNMENT = template.Properties.DESIGNATION_FUNCTIONALASSIGNMENT.ToString();
        parts.DESIGNATION_PLANT = template.Properties.DESIGNATION_PLANT.ToString();
        parts.DESIGNATION_PLACEOFINSTALLATION = template.Properties.DESIGNATION_PLACEOFINSTALLATION.ToString();
        parts.DESIGNATION_LOCATION = template.Properties.DESIGNATION_LOCATION.ToString();
        parts.DESIGNATION_DOCTYPE = template.Properties.DESIGNATION_DOCTYPE.ToString();
        parts.PAGE_COUNTER = counter.ToString(CultureInfo.InvariantCulture);
#pragma warning disable CA2000 // Spikes S0–S10 never disposed a new Page before the commit (as EplanPageWriter).
        var page = new Page();
#pragma warning restore CA2000
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, parts);
        return page;
    }

    private static Page Find(Project project, long objectIdentifier)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).First(pg => pg.ObjectIdentifier == objectIdentifier);
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

    private sealed class Recorded(string listenedFor, string name, string parameter, int thread, double ms, string step)
    {
        public string ListenedFor { get; } = listenedFor;

        public string Name { get; } = name;

        public string Parameter { get; } = parameter;

        public int Thread { get; } = thread;

        public double Ms { get; } = ms;

        public string Step { get; } = step;
    }

    private sealed class Params
    {
        public string Report { get; set; } = string.Empty;

        public string Mode { get; set; } = string.Empty;

        public string Project { get; set; } = string.Empty;
    }
}
