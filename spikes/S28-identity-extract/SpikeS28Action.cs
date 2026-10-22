using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S28;

/// <summary>
/// Spike S28 (M6 design): whether <c>StorableObject.ObjectIdentifier</c> is the same for the same object in two file
/// copies of one project, and how long reading a whole project takes, on the main thread through the dispatcher.
/// Read only: nothing is written.
/// <para>Usage: <c>EplanMcpSpikeS28 /Mode:identity|extract /Report:"…\x.json" /Part:"a.elk|b.elk"</c>.</para>
/// </summary>
public sealed class SpikeS28Action : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS28Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S28";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var p = new Params { Report = Parameter(oActionCallingContext, "Report"), Mode = Parameter(oActionCallingContext, "Mode"), Part = Parameter(oActionCallingContext, "Part") };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S28-identity-extract", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(600), new Func<bool>(() =>
            {
                try
                {
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        Projects(p.Part, out var projects, out var missing);
                        report["missing"] = new JArray(missing);
                        report[p.Mode] = p.Mode switch
                        {
                            "identity" => Identity(projects),
                            "extract" => new JArray(projects.Select(Extract)),
                            _ => "unknown mode",
                        };
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

    private static void Projects(string paths, out List<Project> projects, out List<string> missing)
    {
        projects = [];
        missing = [];
        using var manager = new ProjectManager();
        Project[] open = manager.OpenProjects;
        foreach (string path in paths.Split('|'))
        {
            Project? project = open.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                missing.Add(path);
            }
            else
            {
                projects.Add(project);
            }
        }
    }

    /// <summary>
    /// For every main function and every page of each project: name, ObjectIdentifier, DatabaseIdentifier and the
    /// runtime string identifier; then how many names have the same ObjectIdentifier in all projects.
    /// </summary>
    private static JObject Identity(List<Project> projects)
    {
        var result = new JObject();
        var maps = new List<Dictionary<string, string>>();
        foreach (Project project in projects)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using var finder = new DMObjectsFinder(project);
            foreach (Function f in finder.GetFunctions(null).Where(f => f.IsMainFunction))
            {
                map["F|" + Raw(() => f.Properties.FUNC_FULLNAME) + "|" + Safe(() => f.Page?.Name)] = f.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
            }

            foreach (Page page in finder.GetPages(null))
            {
                map["P|" + page.Name] = page.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
            }

            Function? first = finder.GetFunctions(null).FirstOrDefault(f => f.IsMainFunction);
            result[project.ProjectName] = new JObject
            {
                ["path"] = project.ProjectLinkFilePath,
                ["keys"] = map.Count,
                ["sample"] = first is null ? JValue.CreateNull() : new JObject
                {
                    ["name"] = Raw(() => first.Properties.FUNC_FULLNAME),
                    ["objectIdentifier"] = first.ObjectIdentifier,
                    ["databaseIdentifier"] = first.DatabaseIdentifier,
                    ["stringIdentifier"] = Safe(() => first.ToStringIdentifier()),
                },
            };
            maps.Add(map);
        }

        if (maps.Count >= 2)
        {
            Dictionary<string, string> a = maps[0];
            Dictionary<string, string> b = maps[1];
            var common = a.Keys.Intersect(b.Keys).ToList();
            var differing = common.Where(k => a[k] != b[k]).ToList();
            result["compare"] = new JObject
            {
                ["keysA"] = a.Count,
                ["keysB"] = b.Count,
                ["common"] = common.Count,
                ["sameObjectIdentifier"] = common.Count - differing.Count,
                ["differentObjectIdentifier"] = differing.Count,
                ["differingSamples"] = new JArray(differing.Take(10).Select(k => k + " : " + a[k] + " vs " + b[k])),
                ["duplicateNamesInA"] = a.Count == a.Keys.Distinct().Count() ? 0 : -1,
            };
        }

        return result;
    }

    /// <summary>How long it takes to list and read a whole project: pages, functions, connections, placements.</summary>
    private static JObject Extract(Project project)
    {
        var result = new JObject { ["project"] = project.ProjectName };
        using var finder = new DMObjectsFinder(project);
        var clock = Stopwatch.StartNew();
        Page[] pages = finder.GetPages(null);
        result["pages"] = new JObject { ["count"] = pages.Length, ["seconds"] = Seconds(clock) };
        clock.Restart();
        Function[] functions = finder.GetFunctions(null);
        result["functions"] = new JObject { ["count"] = functions.Length, ["seconds"] = Seconds(clock) };
        clock.Restart();
        Connection[] connections = finder.GetConnections(null);
        result["connections"] = new JObject { ["count"] = connections.Length, ["seconds"] = Seconds(clock) };
        clock.Restart();
        Placement[] placements = finder.GetPlacements(null);
        result["placements"] = new JObject { ["count"] = placements.Length, ["seconds"] = Seconds(clock) };

        clock.Restart();
        int read = 0;
        foreach (Function f in functions)
        {
            _ = Raw(() => f.Properties.FUNC_FULLNAME);
            _ = Safe(() => f.Page?.Name);
            _ = Safe(() => f.Location.X.ToString(CultureInfo.InvariantCulture));
            _ = f.ObjectIdentifier;
            read++;
        }

        result["functionReads"] = new JObject { ["count"] = read, ["seconds"] = Seconds(clock) };
        clock.Restart();
        int ends = 0;
        foreach (Connection c in connections)
        {
            _ = Safe(() => c.StartSymbolReference?.ObjectIdentifier.ToString(CultureInfo.InvariantCulture));
            _ = Safe(() => c.EndSymbolReference?.ObjectIdentifier.ToString(CultureInfo.InvariantCulture));
            ends++;
        }

        result["connectionEndReads"] = new JObject { ["count"] = ends, ["seconds"] = Seconds(clock) };
        return result;
    }

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

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

    private static string Safe(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
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
    }
}
