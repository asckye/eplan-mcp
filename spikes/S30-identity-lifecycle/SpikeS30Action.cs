using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S30;

/// <summary>
/// Spike S30 (M6 design 4.6): what happens to <c>StorableObject.ObjectIdentifier</c> over a project's life, on the main
/// thread through the dispatcher. <c>guid</c> reads <c>PROJ_GUID</c> of open projects; <c>reopen</c> closes a project
/// under the scratch root and opens the same file again; <c>recreate</c> deletes a page and creates new ones, once
/// rolled back and once committed; <c>basic</c> creates two projects from the same basic project and compares them.
/// <para>Usage: <c>EplanMcpSpikeS30 /Mode:guid|reopen|recreate|basic /Report:"…\x.json" /Project:"a.elk|b.elk" /Part:"…\template.zw9"</c>.</para>
/// </summary>
public sealed class SpikeS30Action : IEplAction
{
    private static readonly string _scratchRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS30Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S30";
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
            Part = Parameter(oActionCallingContext, "Part"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S30-identity-lifecycle", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
                        report[p.Mode] = p.Mode switch
                        {
                            "guid" => new JArray(p.Project.Split('|').Select(path => Describe(Open(path)))),
                            "reopen" => Reopen(p.Project),
                            "recreate" => Recreate(Open(p.Project)),
                            "basic" => Basic(p.Part, p.Project),
                            "close" => new JArray(p.Project.Split('|').Select(Close)),
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

    private static Project Open(string path)
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.FirstOrDefault(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("not open: " + path);
    }

    private static void InScratch(string path)
    {
        if (!Path.GetFullPath(path).StartsWith(_scratchRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("this probe works only under " + _scratchRoot);
        }
    }

    private static JObject Describe(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        Page? first = finder.GetPages(null).FirstOrDefault();
        return new JObject
        {
            ["project"] = project.ProjectName,
            ["path"] = project.ProjectLinkFilePath,
            ["guid"] = Safe(() => project.Properties.PROJ_GUID.ToString()),
            ["pages"] = finder.GetPages(null).Length,
            ["firstPage"] = first is null ? JValue.CreateNull() : new JObject
            {
                ["name"] = first.Name,
                ["objectIdentifier"] = first.ObjectIdentifier,
                ["databaseIdentifier"] = first.DatabaseIdentifier,
            },
        };
    }

    /// <summary>Closes the project and opens the same file again; compares every main function's and page's identifier.</summary>
    private static JObject Reopen(string path)
    {
        InScratch(path);
        Project project = Open(path);
        JObject before = Describe(project);
        Dictionary<string, string> a = Map(project);
        var clock = Stopwatch.StartNew();
        project.Close();
        double closeSeconds = Seconds(clock);
        clock.Restart();
        Project reopened;
        using (var manager = new ProjectManager())
        {
            reopened = manager.OpenProject(path);
        }

        double openSeconds = Seconds(clock);
        Dictionary<string, string> b = Map(reopened);
        return new JObject
        {
            ["before"] = before,
            ["after"] = Describe(reopened),
            ["closeSeconds"] = closeSeconds,
            ["openSeconds"] = openSeconds,
            ["compare"] = Compare(a, b),
        };
    }

    /// <summary>
    /// Creates page A, deletes it, creates page B with a new name and page C with A's name, and records their identifiers:
    /// first inside one SafetyPoint that is rolled back, then committed step by step (the new pages are removed at the end).
    /// </summary>
    private static JObject Recreate(Project project)
    {
        InScratch(project.ProjectLinkFilePath);
        var result = new JObject { ["pagesBefore"] = Pages(project) };
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result["rolledBack"] = Cycle(project, 941);
            safety.Rollback();
        }

        result["pagesAfterRollback"] = Pages(project);
        var committed = new JObject();
        long a, b, c;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            Page page = NewPage(project, 951);
            a = page.ObjectIdentifier;
            committed["a"] = Entry(page);
            safety.Commit();
        }

        using (SafetyPoint safety = SafetyPoint.Create())
        {
            Find(project, a).Remove();
            safety.Commit();
        }

        using (SafetyPoint safety = SafetyPoint.Create())
        {
            Page pageB = NewPage(project, 952);
            Page pageC = NewPage(project, 951);
            b = pageB.ObjectIdentifier;
            c = pageC.ObjectIdentifier;
            committed["b"] = Entry(pageB);
            committed["c"] = Entry(pageC);
            safety.Commit();
        }

        committed["bReusesA"] = b == a;
        committed["cReusesA"] = c == a;
        result["committed"] = committed;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            Find(project, b).Remove();
            Find(project, c).Remove();
            safety.Commit();
        }

        result["pagesAfter"] = Pages(project);
        return result;
    }

    private static JObject Cycle(Project project, int counter)
    {
        Page pageA = NewPage(project, counter);
        long a = pageA.ObjectIdentifier;
        var o = new JObject { ["a"] = Entry(pageA) };
        pageA.Remove();
        Page pageB = NewPage(project, counter + 1);
        Page pageC = NewPage(project, counter);
        o["b"] = Entry(pageB);
        o["c"] = Entry(pageC);
        o["bReusesA"] = pageB.ObjectIdentifier == a;
        o["cReusesA"] = pageC.ObjectIdentifier == a;
        return o;
    }

    /// <summary>
    /// Creates two projects from <paramref name="template"/> in <paramref name="folder"/>, compares their identifiers and
    /// PROJ_GUID, tries to open the first a second time while the API holds it, and closes both.
    /// </summary>
    private static JObject Basic(string template, string folder)
    {
        InScratch(template);
        InScratch(folder);
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var created = new List<Project>();
        var result = new JObject();
        var maps = new List<Dictionary<string, string>>();
        var projects = new JArray();
        using (var manager = new ProjectManager())
        {
            for (int i = 1; i <= 2; i++)
            {
                string link = Path.Combine(folder, $"S30_basic{i}_{stamp}.elk");
                var clock = Stopwatch.StartNew();
                Project project = manager.CreateProject(link, template);
                double seconds = Seconds(clock);
                created.Add(project);
                maps.Add(Map(project));
                JObject entry = Describe(project);
                entry["createSeconds"] = seconds;
                entry["keys"] = maps[i - 1].Count;
                projects.Add(entry);
            }
        }

        // r2: whether the new projects are still open once their ProjectManager is disposed, and again after a second
        // OpenProject of the first one (r1 saw S063045 there, and Close() then failed with S063080 for both).
        result["openAfterManagerDisposed"] = new JArray(created.Select(pr => IsOpen(pr.ProjectLinkFilePath)));
        try
        {
            using var manager = new ProjectManager();
            Project again = manager.OpenProject(created[0].ProjectLinkFilePath);
            result["openAgain"] = new JObject { ["sameObject"] = ReferenceEquals(again, created[0]), ["name"] = again.ProjectName, ["path"] = again.ProjectLinkFilePath };
        }
        catch (Exception ex)
        {
            result["openAgain"] = Describe(ex);
        }

        result["openAfterOpenAgain"] = new JArray(created.Select(pr => IsOpen(pr.ProjectLinkFilePath)));
        result["projects"] = projects;
        result["compare"] = Compare(maps[0], maps[1]);
        var closed = new JArray();
        foreach (Project project in created)
        {
            try
            {
                project.Close();
                closed.Add(project.ProjectLinkFilePath);
            }
            catch (Exception ex)
            {
                closed.Add(Describe(ex));
            }
        }

        result["closed"] = closed;
        return result;
    }

    /// <summary>r3: closes an open project under the scratch root through a fresh lookup in OpenProjects.</summary>
    private static JObject Close(string path)
    {
        InScratch(path);
        try
        {
            Open(path).Close();
            return new JObject { ["path"] = path, ["closed"] = true, ["openAfter"] = IsOpen(path) };
        }
        catch (Exception ex)
        {
            return new JObject { ["path"] = path, ["error"] = Describe(ex) };
        }
    }

    private static bool IsOpen(string path)
    {
        using var manager = new ProjectManager();
        return manager.OpenProjects.Any(o => string.Equals(o.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Main functions by "name|page" and pages by name, to their ObjectIdentifier (as spike S28).</summary>
    private static Dictionary<string, string> Map(Project project)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var finder = new DMObjectsFinder(project);
        foreach (Function f in finder.GetFunctions(null).Where(f => f.IsMainFunction))
        {
            map["F|" + Safe(() => f.Properties.FUNC_FULLNAME.ToString()) + "|" + Safe(() => f.Page?.Name)] = f.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
        }

        foreach (Page page in finder.GetPages(null))
        {
            map["P|" + page.Name] = page.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
        }

        return map;
    }

    private static JObject Compare(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        var common = a.Keys.Intersect(b.Keys).ToList();
        var differing = common.Where(k => a[k] != b[k]).ToList();
        return new JObject
        {
            ["keysA"] = a.Count,
            ["keysB"] = b.Count,
            ["common"] = common.Count,
            ["sameObjectIdentifier"] = common.Count - differing.Count,
            ["differentObjectIdentifier"] = differing.Count,
            ["differingSamples"] = new JArray(differing.Take(10).Select(k => k + " : " + a[k] + " vs " + b[k])),
        };
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

    private static JObject Entry(Page page) => new() { ["name"] = page.Name, ["objectIdentifier"] = page.ObjectIdentifier };

    private static int Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).Length;
    }

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

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

        public string Project { get; set; } = string.Empty;

        public string Part { get; set; } = string.Empty;
    }
}
