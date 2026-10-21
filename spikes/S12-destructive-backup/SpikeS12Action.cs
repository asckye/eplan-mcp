using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.EServices;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S12;

/// <summary>
/// Spike S12 (M4 design 4.6, ADR-0013): the APIs behind the Destructive tools and their backup, run the way the Bridge
/// runs a tool (main thread through the dispatcher, after the scheduling action has returned, writes in SafetyPoint +
/// UndoStep + QuietModeStep), as S10 and S11 did: whether <c>CopyProject(Snapshot)</c> copies an open project, what
/// <c>Backup.Project(MakeBackup)</c> does to it, and whether page removal and renumbering roll back and undo in one step.
/// <para>
/// Usage: <c>EplanMcpSpikeS12 /Mode:projects|select|restore|count|copy|copyall|backup|prepare|removerollback|remove|renumberdevicesrollback|renumberdevices|backuprenumber|renumberfilter|renumberpagesrollback|renumberpages|close
/// /Report:"…\x.json" /ScratchRoot:"…\s12" /Name:project [/Out:"…\path"] [/Restore:name] [/Macro:"…\x.ema"] [/Scheme:name]</c>.
/// Works only on the open project called Name under ScratchRoot.
/// </para>
/// </summary>
public sealed class SpikeS12Action : IEplAction
{
    // Report (evaluation) page types are the DocumentType values 9-33 and 35-37 (reflection), plus a few later ones (S11).
    private static readonly int[] _reportTypes = [.. Enumerable.Range(9, 25), 35, 36, 37, 39, 41, 42, 44, 45, 46, 47, 48, 50, 52, 55, 56, 59, 60, 62, 63];

    // The pages "prepare" creates; the macro goes on the first one (S10 used the same structure).
    private static readonly int[] _probeCounters = [90, 91, 92];

    // Numbering scheme names tried in the rollback mode; "" is documented for the filter only, so it is tried, not assumed.
    private static readonly string[] _schemeCandidates = [string.Empty, "Standard", "Default"];

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS12Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S12";
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
            Name = Parameter(oActionCallingContext, "Name"),
            Out = Parameter(oActionCallingContext, "Out"),
            Restore = Parameter(oActionCallingContext, "Restore"),
            Macro = Parameter(oActionCallingContext, "Macro"),
            Scheme = Parameter(oActionCallingContext, "Scheme"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S12-destructive-backup", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
        // Ctrl+Z works on the current project: the driver undoes only while every open project is a scratch copy and the
        // spike's copy is current (S10).
        Project[] open = OpenProjects();
        string scratchHome = Path.GetDirectoryName(Path.GetDirectoryName(p.ScratchRoot.TrimEnd('\\'))) ?? p.ScratchRoot;
        report["openOutsideScratch"] = open.Count(o => !IsUnder(o.ProjectFullName, scratchHome));
        report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
        if (p.Mode == "projects")
        {
            report["open"] = new JArray(open.Select(o => o.ProjectName));
            return;
        }

        if (p.Mode == "restore")
        {
            Project? previous = open.FirstOrDefault(o => o.ProjectName == p.Restore);
            using var edit = new Edit();
            report["restored"] = previous is not null && edit.SelectProjectInPagesNavigator(previous);
            return;
        }

        Project? project = open.FirstOrDefault(o => o.ProjectName == p.Name && IsUnder(o.ProjectFullName, p.ScratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project " + p.Name + " under the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        report["fullName"] = project.ProjectFullName;
        report["linkFile"] = Try(() => project.ProjectLinkFilePath);
        switch (p.Mode)
        {
            case "select":
                using (var edit = new Edit())
                {
                    report["select"] = edit.SelectProjectInPagesNavigator(project);
                }

                report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
                break;
            case "count":
                report["state"] = State(project);
                break;
            case "copy":
            case "copyall":
                ProjectManager.CopyMode mode = p.Mode == "copy" ? ProjectManager.CopyMode.Snapshot : ProjectManager.CopyMode.All;
                report["copyMode"] = mode.ToString();
                report["copy"] = Try(() => Timed(() =>
                {
                    using var manager = new ProjectManager();
                    manager.CopyProject(project.ProjectLinkFilePath, p.Out + ".elk", mode);
                    return new JObject { ["destination"] = p.Out };
                }));
                report["stillOpen"] = OpenProjects().Any(o => o.ProjectName == project.ProjectName);
                break;
            case "backup":
                report["backup"] = Try(() => Timed(() =>
                {
                    new Backup().Project(project.ProjectFullName, "S12", p.Out, "S12_Backup", Backup.Type.MakeBackup, false);
                    return new JObject { ["target"] = p.Out };
                }));
                report["stillOpen"] = OpenProjects().Any(o => o.ProjectName == project.ProjectName);
                break;
            case "prepare":
                report["prepare"] = Try(() => Scoped("S12 prepare", () =>
                {
                    Page[] pages = [.. _probeCounters.Select(n => NewPage(project, n))];
                    int placed = InsertMacro(project, pages[0], p.Macro, 40, 200).Length;
                    return new JObject { ["pages"] = new JArray(pages.Select(pg => pg.Name)), ["placed"] = placed, ["questionTags"] = new JArray(QuestionTags(project)) };
                }));
                report["state"] = State(project);
                break;
            case "removerollback":
                report["remove"] = Try(() => RolledBack(() => RemoveCrossReferencedPage(project), () => new JObject { ["state"] = State(project) }));
                break;
            case "remove":
                report["remove"] = Try(() => Scoped("S12 remove page", () => RemoveCrossReferencedPage(project)));
                report["state"] = State(project);
                break;
            case "renumberdevicesrollback":
                var attempts = new JArray();
                foreach (string scheme in _schemeCandidates)
                {
                    attempts.Add(new JObject
                    {
                        ["scheme"] = scheme,
                        ["result"] = Try(() => RolledBack(() => RenumberDevices(project, scheme), () => new JObject { ["questionTags"] = new JArray(QuestionTags(project)) })),
                    });
                }

                report["attempts"] = attempts;
                break;
            case "backuprenumber":
                // r3: the Destructive pipeline's backup (copy, open the copy read-only, count its pages, close it) and then a
                // renumbering in the same dispatch, as eplan_device_renumber does; EPLAN died in Renumber.DeviceTags there.
                report["backup"] = Try(() => BackUpAndCount(project, p.Out));
                report["currentAfterBackup"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
                report["renumber"] = Try(() => Scoped("S12 renumber after backup", () => RenumberDevices(project, string.Empty)));
                report["state"] = State(project);
                break;
            case "renumberfilter":
                // r4: which functions make Renumber.DeviceTags(PostNumerate) raise an access violation (r3 killed EPLAN).
                report["renumber"] = Try(() => RolledBack(() => RenumberFiltered(project, p.Scheme), () => new JObject { ["questionTags"] = new JArray(QuestionTags(project)) }));
                break;
            case "renumberdevices":
                report["renumber"] = Try(() => Scoped("S12 renumber devices", () => RenumberDevices(project, p.Scheme)));
                report["state"] = State(project);
                break;
            case "renumberpagesrollback":
                report["renumber"] = Try(() => RolledBack(() => RenumberPages(project), () => new JObject { ["probePages"] = new JArray(ProbePages(project).Select(pg => pg.Name)) }));
                break;
            case "renumberpages":
                report["renumber"] = Try(() => Scoped("S12 renumber pages", () => RenumberPages(project)));
                report["state"] = State(project);
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

    /// <summary>
    /// Removes the circuit page (among the first 60) whose main functions have the most auxiliary functions on other pages,
    /// and reports what is left of those devices elsewhere.
    /// </summary>
    private static JObject RemoveCrossReferencedPage(Project project)
    {
        Function[] all = Functions(project, null);
        Page? target = null;
        string[] crossNames = [];
        foreach (Page page in Pages(project).Where(pg => pg.PageType == DocumentTypeManager.DocumentType.Circuit).Take(60))
        {
            string[] names = [.. Functions(project, page).Where(f => f.IsMainFunction && f.Name.Length > 0).Select(f => f.Name).Distinct()];
            string[] crossed = [.. names.Where(n => all.Any(f => f.Name == n && f.Page != page))];
            if (crossed.Length > crossNames.Length)
            {
                target = page;
                crossNames = crossed;
            }
        }

        if (target is null)
        {
            throw new InvalidOperationException("no circuit page with cross-referenced devices among the first 60");
        }

        string name = target.Name;
        int onPage = Functions(project, target).Length;
        JObject before = State(project);
        target.Remove();
        Function[] left = Functions(project, null);
        return new JObject
        {
            ["page"] = name,
            ["functionsOnPage"] = onPage,
            ["crossReferenced"] = new JArray(crossNames.Take(20)),
            ["before"] = before,
            ["after"] = State(project),
            ["leftElsewhere"] = new JArray(crossNames.Take(20).Select(n => new JObject
            {
                ["name"] = n,
                ["functions"] = left.Count(f => f.Name == n),
                ["mainLeft"] = left.Any(f => f.Name == n && f.IsMainFunction),
            })),
        };
    }

    /// <summary>
    /// PostNumerate on the functions of /91 that match the filter passed as /Scheme: "device" ('?' in the device tag before
    /// ':'), "terminal" ('?' only after ':'), "nonquestion" (no '?'), anything else all of them.
    /// </summary>
    private static JObject RenumberFiltered(Project project, string filter)
    {
        Page page = ProbePages(project).FirstOrDefault(pg => pg.Name == "=GAA+A1&EFS1/91") ?? throw new InvalidOperationException("page /91 missing");
        Function[] all = Functions(project, page);
        bool DeviceQuestion(Function f) => f.Name.Split(':')[0].Contains("?");
        bool TerminalQuestion(Function f) => !DeviceQuestion(f) && f.Name.Contains("?");
        // "zero-<filter>": the same functions without PostNumerate, as eplan_device_renumber with only_unnumbered false.
        bool zero = filter.StartsWith("zero-", StringComparison.Ordinal);
        string which = zero ? filter.Substring(5) : filter;
        Function[] chosen = which switch
        {
            "device" => [.. all.Where(DeviceQuestion)],
            "terminal" => [.. all.Where(TerminalQuestion)],
            "nonquestion" => [.. all.Where(f => !f.Name.Contains("?"))],
            "nonterminal" => [.. all.Where(f => !TerminalQuestion(f))],
            _ => all,
        };
        var report = new JObject { ["filter"] = filter, ["chosen"] = new JArray(chosen.Select(f => f.Name + (f.IsMainFunction ? " (main)" : string.Empty))) };
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "s12r4-before-renumber.json"), report.ToString(Formatting.Indented));
        using var renumber = new Renumber();
        renumber.DeviceTags([.. chosen.Cast<IFunctionBase>()], null, zero ? default : Renumber.Enums.DeviceTags.PostNumerate, string.Empty, 1, 1);
        report["after"] = new JArray(Functions(project, page).Select(f => f.Name).Distinct());
        return report;
    }

    private static JObject BackUpAndCount(Project project, string copy)
    {
        using var manager = new ProjectManager();
        manager.CopyProject(project.ProjectLinkFilePath, copy + ".elk", ProjectManager.CopyMode.Snapshot);
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            Project opened = manager.OpenProject(copy + ".elk", ProjectManager.OpenMode.ReadOnly);
            int pages;
            using (var finder = new DMObjectsFinder(opened))
            {
                pages = finder.GetPages(null).Length;
            }

            opened.Close();
            return new JObject { ["pages"] = pages };
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }

    private static JObject RenumberDevices(Project project, string scheme)
    {
        // r3: the page eplan_device_renumber failed on, when the project has it.
        Page page = ProbePages(project).FirstOrDefault(pg => pg.Name == "=GAA+A1&EFS1/91") ?? ProbePages(project).FirstOrDefault() ?? throw new InvalidOperationException("probe pages missing; run 'prepare' first");
        Function[] functions = Functions(project, page);
        string[] before = [.. functions.Select(f => f.Name)];
        using var renumber = new Renumber();
        renumber.DeviceTags([.. functions.Cast<IFunctionBase>()], null, Renumber.Enums.DeviceTags.PostNumerate, scheme, 1, 1);
        return new JObject
        {
            ["page"] = page.Name,
            ["before"] = new JArray(before.Distinct()),
            ["after"] = new JArray(Functions(project, page).Select(f => f.Name).Distinct()),
            ["questionTagsInProject"] = new JArray(QuestionTags(project)),
        };
    }

    private static JObject RenumberPages(Project project)
    {
        Page[] pages = ProbePages(project);
        string[] before = [.. pages.Select(pg => pg.Name)];
        using var renumber = new Renumber();
        renumber.Pages(pages, true, 200, 10, false, false, Renumber.Enums.SubPages.Retain);
        return new JObject { ["before"] = new JArray(before), ["after"] = new JArray(pages.Select(pg => pg.Name)) };
    }

    private static JObject State(Project project)
    {
        Page[] pages = Pages(project);
        return new JObject
        {
            ["pages"] = pages.Length,
            ["reportPages"] = pages.Count(pg => IsReport(pg.PageType.ToString())),
            ["functions"] = Functions(project, null).Length,
            ["questionTags"] = QuestionTags(project).Length,
            ["probePages"] = new JArray(ProbePages(project).Select(pg => pg.Name)),
            ["messages"] = new PrjMessagesCollection(project).Count,
        };
    }

    private static string[] QuestionTags(Project project) =>
        [.. Functions(project, null).Select(f => f.Name).Where(n => n.Contains("?")).Distinct().Take(40)];

    // The probe pages keep their place in the structure when renumbered, so they are the =GAA+A1&EFS1 pages from /90 up.
    private static Page[] ProbePages(Project project) =>
        [.. Pages(project).Where(pg => pg.Name.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal)
            && int.TryParse(pg.Name.Substring("=GAA+A1&EFS1/".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 90)];

    private static bool IsReport(string type) =>
        Enum.TryParse(type, out DocumentTypeManager.DocumentType parsed) && _reportTypes.Contains((int)parsed);

    /// <summary>The Bridge's TransactionScope (M3 design 4.2): SafetyPoint, the UndoStep inside it, commit, close.</summary>
    private static JToken Scoped(string description, Func<JObject> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        JToken result;
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
            result = Timed(change);
        }

        safety.Commit();
        undo.SetUndoDescription(description);
        undo.CloseUndo();
        return result;
    }

    private static JObject RolledBack(Func<JObject> change, Func<JObject> afterwards)
    {
        JObject result;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result = Timed(change);
            safety.Rollback();
        }

        result["afterRollback"] = afterwards();
        return result;
    }

    private static JObject Timed(Func<JObject> work)
    {
        var clock = Stopwatch.StartNew();
        JObject result = work();
        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        return result;
    }

    private static StorableObject[] InsertMacro(Project project, Page page, string macroPath, double x, double y)
    {
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        using var insert = new Insert();
        return insert.WindowMacro(macro, WindowMacro.Enums.RepresentationType.MultiLine, 0, page, new PointD(x, y), Insert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.NumberWithQuestionMark);
    }

    private static Page NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_PLANT = "GAA";
        properties.DESIGNATION_LOCATION = "A1";
        properties.DESIGNATION_DOCTYPE = "EFS1";
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
        return page;
    }

    private static Page[] Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null);
    }

    private static Function[] Functions(Project project, Page? page)
    {
        using var finder = new DMObjectsFinder(project);
        if (page is null)
        {
            return finder.GetFunctions(null);
        }

        using var filter = new FunctionsFilter { Page = page };
        return finder.GetFunctions(filter);
    }

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
        var o = new JObject { ["error"] = true, ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
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

        public string Name { get; set; } = string.Empty;

        public string Out { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;

        public string Macro { get; set; } = string.Empty;

        public string Scheme { get; set; } = string.Empty;
    }
}
