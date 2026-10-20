using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S10;

/// <summary>
/// Spike S10 (M3b design 4.5). Runs the M3b writes the way the Bridge runs a tool: on EPLAN's main thread through the
/// WPF dispatcher, after the action that scheduled them has returned, in LockingStep, SafetyPoint, UndoStep and
/// QuietModeStep (M3 design 4.2). S9 ran everything inside an action.
/// <para>
/// Usage: <c>EplanMcpSpikeS10 /Mode:noop|pages|macroinfo|numbering|undomacro|connect|symbol|count|close /Report:"…\x.json"
/// /ScratchRoot:"…\scratch" [/Macro:"…\x.ema"]</c>. The action only schedules the probes and returns; the report appears
/// when they are done (written as <c>.partial</c> first, then moved), so the driver waits for it. Works only on an open
/// project under ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS10Action : IEplAction
{
    // Probe pages, all under =GAA+A1&EFS1 of the sample project: 70-74 numbering, 75 undo of a macro, 76 connections,
    // 77 a function from a symbol.
    private const int FirstCounter = 70;
    private const int PageCount = 8;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS10Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S10";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string reportPath = Parameter(oActionCallingContext, "Report");
        string scratchRoot = Parameter(oActionCallingContext, "ScratchRoot");
        string mode = Parameter(oActionCallingContext, "Mode");
        string macroPath = Parameter(oActionCallingContext, "Macro");
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, reportPath, scratchRoot, mode, macroPath)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, string reportPath, string scratchRoot, string mode, string macroPath)
    {
        var report = new JObject
        {
            ["spike"] = "S10-dispatcher-writes",
            ["mode"] = mode,
            ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        };
        try
        {
            Thread.Sleep(300); // let the scheduling action return first: the probes must not run inside it
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                // Everything is caught in here: an exception escaping a dispatched delegate ends EPLAN (S1).
                try
                {
                    report["insideAction"] = false;
                    using (new LockingStep())
                    using (new QuietModeStep(QuietModes.ShowNoDialogs))
                    {
                        Run(report, reportPath, scratchRoot, mode, macroPath);
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
                report["fatal"] = "the dispatcher did not run the probes within 120 s";
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }

        report.Remove("inProgress");
        File.WriteAllText(reportPath + ".partial", report.ToString(Formatting.Indented));
        File.Delete(reportPath);
        File.Move(reportPath + ".partial", reportPath);
    }

    private static void Run(JObject report, string reportPath, string scratchRoot, string mode, string macroPath)
    {
        // Before an undo the driver checks that nothing but scratch copies is open and that this copy is the current
        // project: Ctrl+Z takes back the current project's last undo step (CLAUDE.md rule 1). ScratchRoot is
        // <data root>\scratch\spikes\s10; two levels up is the data root's scratch folder.
        Project[] open = OpenProjects();
        string scratchHome = Path.GetDirectoryName(Path.GetDirectoryName(scratchRoot.TrimEnd('\\'))) ?? scratchRoot;
        report["openOutsideScratch"] = open.Count(p => !IsUnder(p.ProjectFullName, scratchHome));
        report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
        if (mode == "projects")
        {
            // Names only, and whether each is a scratch copy: never changed here.
            report["open"] = new JArray(open.Select(p => new JObject { ["name"] = p.ProjectName, ["underScratchHome"] = IsUnder(p.ProjectFullName, scratchHome) }));
            return;
        }

        if (mode == "restore")
        {
            // Selects again the project that was current before "select" (r2: the eval fixture, a scratch copy too);
            // its name comes in /Macro.
            Project? previous = open.FirstOrDefault(p => p.ProjectName == macroPath);
            report["restored"] = previous is not null && new Edit().SelectProjectInPagesNavigator(previous);
            report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
            return;
        }

        Project? project = OpenProjects().FirstOrDefault(p => IsUnder(p.ProjectFullName, scratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectFullName;
        void Probe(string name, Func<JToken> probe) => ProbeInto(report, reportPath, name, probe);
        switch (mode)
        {
            case "noop":
                break;
            case "select":
                // Ctrl+Z works on the current project (r2: another scratch copy was current), so the copy is selected
                // in the page navigator first; "restore" selects the previous one again.
                Probe("select", () => new Edit().SelectProjectInPagesNavigator(project));
                report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
                break;
            case "pages":
                Probe("create_probe_pages", () => Scoped("S10 pages", () => new JArray(Enumerable.Range(FirstCounter, PageCount).Select(n => NewPage(project, n).Name))));
                Probe("page_size_new", () => DescribeSize(ProbePage(project, FirstCounter)));
                Probe("page_size_sample", () => DescribeSize(PageNamed(project, "=GAA+A1&EFS1/1") ?? throw new InvalidOperationException("sample page missing")));
                break;
            case "macroinfo":
                Probe("functions_before_open", () => Functions(project, null).Length);
                Probe("macro", () => DescribeMacro(project, macroPath));
                Probe("functions_after_open", () => Functions(project, null).Length);
                break;
            case "numbering":
                Numbering(project, macroPath, Probe);
                break;
            case "undomacro":
                Probe("insert_for_undo", () => Scoped("S10 macro", () =>
                {
                    Page page = ProbePage(project, FirstCounter + 5);
                    int placed = InsertMacro(project, page, macroPath, 40, 200, WindowMacro.Enums.NumerationMode.Number).Length;
                    return new JObject { ["placed"] = placed, ["onPage"] = Functions(project, page).Length };
                }));
                Probe("state", () => State(project));
                break;
            case "connect":
                Connect(project, macroPath, Probe);
                break;
            case "symbol":
                Probe("place_coil_undo_step_around_transaction", () => PlaceCoil(project, ProbePage(project, FirstCounter + 7)));
                Probe("state", () => State(project));

                // Whether the tags show on the drawing: the coil's page and a page from a numbered macro, as images.
                Probe("export_images", () =>
                {
                    string directory = Path.Combine(Path.GetDirectoryName(reportPath)!, "s10-images");
                    using var export = new Export();
                    foreach (int counter in new[] { FirstCounter + 7, FirstCounter + 2 })
                    {
                        export.GraphicPage(ProbePage(project, counter), directory, Export.Format.PNG, 24, 2000, Export.Compression.None, false);
                    }

                    return new JArray(Directory.EnumerateFiles(directory).Select(Path.GetFileName));
                });
                break;
            case "count":
                Probe("state", () => State(project));
                break;
            case "close":
                Probe("close", () =>
                {
                    project.Close();
                    return "closed";
                });
                break;
            default:
                report["refused"] = "unknown mode " + mode;
                break;
        }
    }

    /// <summary>
    /// Item 1: the same macro, whose device tags then already exist, inserted once per numeration mode, each on its own
    /// page. A tag with more than one main function afterwards is a merge.
    /// </summary>
    private static void Numbering(Project project, string macroPath, Action<string, Func<JToken>> probe)
    {
        (string Name, WindowMacro.Enums.NumerationMode Mode)[] runs =
        [
            ("first_none", WindowMacro.Enums.NumerationMode.None),
            ("again_none", WindowMacro.Enums.NumerationMode.None),
            ("again_number", WindowMacro.Enums.NumerationMode.Number),
            ("again_number_prefer_prefix", WindowMacro.Enums.NumerationMode.NumberPreferPrefix),
            ("again_number_with_question_mark", WindowMacro.Enums.NumerationMode.NumberWithQuestionMark),
        ];
        for (int i = 0; i < runs.Length; i++)
        {
            (string name, WindowMacro.Enums.NumerationMode mode) = runs[i];
            int counter = FirstCounter + i;
            probe(name, () =>
            {
                HashSet<string> before = new(MainNames(project), StringComparer.Ordinal);
                return Scoped("S10 numbering", () =>
                {
                    Page page = ProbePage(project, counter);
                    InsertMacro(project, page, macroPath, 40, 200, mode);
                    Function[] inserted = Functions(project, page);
                    string[] all = MainNames(project);
                    return new JObject
                    {
                        ["mode"] = mode.ToString(),
                        ["page"] = page.Name,
                        ["functions"] = new JArray(inserted.Select(f => new JObject { ["name"] = f.Name, ["visibleName"] = f.VisibleName, ["main"] = f.IsMainFunction, ["category"] = f.FunctionCategory.ToString() })),
                        ["tags"] = new JArray(inserted.Select(f => f.Name).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Select(n => new JObject
                        {
                            ["tag"] = n,
                            ["existedBefore"] = before.Contains(n),
                            ["mainFunctionsNow"] = all.Count(a => a == n),
                        })),
                    };
                });
            });
        }
    }

    /// <summary>Items 2 and 3: undoing and rolling back Generate.Connections.</summary>
    private static void Connect(Project project, string macroPath, Action<string, Func<JToken>> probe)
    {
        Page page = ProbePage(project, FirstCounter + 6);
        probe("insert_macro", () => Scoped("S10 macro for connections", () => InsertMacro(project, page, macroPath, 40, 200, WindowMacro.Enums.NumerationMode.Number).Length));
        probe("generate_rolled_back", () => RolledBack(project, page, rebuild: false));

        // "Update only" on a page whose connections are already there: does it add or remove any?
        Page sample = PageNamed(project, "=GAA+A1&EFS1/1") ?? throw new InvalidOperationException("sample page missing");
        probe("generate_existing_page_update", () => RolledBack(project, sample, rebuild: false));
        probe("generate_existing_page_rebuild", () => RolledBack(project, sample, rebuild: true));

        // Committed last, so that one undo takes back this step only.
        probe("generate_committed", () => Scoped("S10 connections", () =>
        {
            int before = ConnectionsOn(project, page).Length;
            using var generate = new Generate();
            generate.Connections([page], false);
            return new JObject { ["before"] = before, ["after"] = ConnectionsOn(project, page).Length };
        }));
        probe("state", () => State(project));
    }

    private static JObject RolledBack(Project project, Page page, bool rebuild)
    {
        string[] before = ConnectionsOn(project, page);
        string[] during;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            using var generate = new Generate();
            generate.Connections([page], rebuild);
            during = ConnectionsOn(project, page);
            safety.Rollback();
        }

        string[] after = ConnectionsOn(project, page);
        return new JObject
        {
            ["page"] = page.Name,
            ["rebuild"] = rebuild,
            ["before"] = before.Length,
            ["during"] = during.Length,
            ["after"] = after.Length,
            ["addedDuring"] = new JArray(during.Except(before).Take(20)),
            ["removedDuring"] = new JArray(before.Except(during).Take(20)),
            ["sameAfterAsBefore"] = before.OrderBy(c => c, StringComparer.Ordinal).SequenceEqual(after.OrderBy(c => c, StringComparer.Ordinal)),
        };
    }

    /// <summary>
    /// Item 7: a function from a library symbol, in the shape S9 left for it (UndoStep around a Transaction, no
    /// SafetyPoint), and whether choosing a property placement scheme shows its tag.
    /// </summary>
    private static JObject PlaceCoil(Project project, Page page)
    {
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        using Transaction transaction = new TransactionManager().CreateTransaction();

        // r1: outside an action nothing is locked implicitly ("object not locked before write access" on the page).
        page.LockObject();
        using var library = new SymbolLibrary(project, "IEC_symbol");
        using var symbol = new Symbol(library, "K");
        var function = new Function();
        function.Create(page, symbol.Variants[0]);
        function.Location = new PointD(60, 160);
        function.Name = "=GAA+A1-KF95";
        var result = new JObject
        {
            ["name"] = function.Name,
            ["visibleNameBefore"] = function.VisibleName,
            ["propertyPlacementsBefore"] = function.PropertyPlacements.Length,
        };
        result["schemes"] = Try(() => new JArray(function.PropertyPlacementsSchemas.All.Select(s => new JObject { ["id"] = s.ID, ["name"] = s.Name, ["custom"] = s.IsCustom })));
        result["selectFirstScheme"] = Try(() =>
        {
            SymbolReference.PropertyPlacementsSchema[] all = function.PropertyPlacementsSchemas.All;
            if (all.Length == 0)
            {
                return "no scheme";
            }

            function.PropertyPlacementsSchemas.Selected = all[0];
            return "selected " + all[0].Name;
        });
        result["visibleNameAfter"] = function.VisibleName;
        result["propertyPlacementsAfter"] = function.PropertyPlacements.Length;
        transaction.Commit();
        undo.SetUndoDescription("S10 coil");
        undo.CloseUndo();
        result["namedAfterCommit"] = Functions(project, null).Count(f => f.Name == "=GAA+A1-KF95");
        return result;
    }

    private static JObject DescribeMacro(Project project, string macroPath)
    {
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        Placement[] objects = macro.Objects;
        return new JObject
        {
            ["representation"] = macro.RepresentationType.ToString(),
            ["representations"] = new JArray(macro.RepresentationTypes.Select(t => t.ToString())),
            ["variants"] = new JArray(macro.GetVariants(WindowMacro.Enums.RepresentationType.MultiLine)),
            ["size"] = Point(macro.Size),
            ["boundingBox"] = new JArray(macro.GetBoundingBox().Select(Point)),
            ["objects"] = objects.Length,
            ["topLevelObjects"] = macro.TopLevelObjects.Length,
            ["types"] = new JArray(objects.Select(o => o.GetType().Name).Distinct()),
            ["functions"] = new JArray(objects.OfType<Function>().Select(f => new JObject { ["name"] = f.Name, ["visibleName"] = f.VisibleName, ["main"] = f.IsMainFunction, ["category"] = f.FunctionCategory.ToString() })),
            ["macroPage"] = Try(() => macro.Page?.Name),
        };
    }

    private static JObject DescribeSize(Page page) => new()
    {
        ["page"] = page.Name,
        ["size"] = Point(page.Size),
        ["boundingBox"] = new JArray(page.GetBoundingBox().Select(Point)),
        ["gridSize"] = page.GridSize,
    };

    private static JObject State(Project project) => new()
    {
        ["functions"] = Functions(project, null).Length,
        ["connections"] = AllConnections(project),
        ["probePages"] = new JArray(Enumerable.Range(FirstCounter, PageCount).Select(n => PageNamed(project, ProbePageName(n)) is Page p
            ? new JObject { ["page"] = p.Name, ["functions"] = Functions(project, p).Length, ["connections"] = ConnectionsOn(project, p).Length }
            : new JObject { ["page"] = ProbePageName(n), ["missing"] = true })),
        ["coil"] = Functions(project, null).Count(f => f.Name == "=GAA+A1-KF95"),
    };

    // --- helpers ---

    /// <summary>The Bridge's TransactionScope (M3 design 4.2): SafetyPoint, the UndoStep inside it, commit, close.</summary>
    private static JToken Scoped(string description, Func<JToken> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        JToken result;
        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
            result = change();
        }

        safety.Commit();
        undo.SetUndoDescription(description);
        undo.CloseUndo();
        return result;
    }

    private static StorableObject[] InsertMacro(Project project, Page page, string macroPath, double x, double y, WindowMacro.Enums.NumerationMode mode)
    {
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        using var insert = new Insert();
        return insert.WindowMacro(macro, WindowMacro.Enums.RepresentationType.MultiLine, 0, page, new PointD(x, y), Insert.MoveKind.Absolute, mode);
    }

    private static string ProbePageName(int counter) => string.Format(CultureInfo.InvariantCulture, "=GAA+A1&EFS1/{0}", counter);

    private static Page ProbePage(Project project, int counter) =>
        PageNamed(project, ProbePageName(counter)) ?? throw new InvalidOperationException("probe page missing; run 'pages' first");

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

    private static Page? PageNamed(Project project, string name)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).FirstOrDefault(p => p.Name == name);
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

    private static string[] MainNames(Project project) => [.. Functions(project, null).Where(f => f.IsMainFunction && f.Name.Length > 0).Select(f => f.Name)];

    // 3D connections have no readable pins (S6); a page's connections are filtered in memory (A11).
    private static string[] ConnectionsOn(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        return [.. finder.GetConnections(null).Where(c => c is not Connection3D)
            .Where(c => c.StartPin?.ParentFunction?.Page == page || c.EndPin?.ParentFunction?.Page == page)
            .Select(c => c.StartPin?.ParentFunction?.Name + ":" + c.StartPin?.Name + " - " + c.EndPin?.ParentFunction?.Name + ":" + c.EndPin?.Name)];
    }

    private static int AllConnections(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetConnections(null).Length;
    }

    private static JArray Point(PointD point) => new(Math.Round(point.X, 2), Math.Round(point.Y, 2));

    private static void ProbeInto(JObject report, string reportPath, string name, Func<JToken> probe)
    {
        report["inProgress"] = name;
        File.WriteAllText(reportPath + ".partial", report.ToString(Formatting.Indented));
        try
        {
            report[name] = probe();
        }
        catch (Exception ex)
        {
            report[name] = new JObject { ["error"] = Describe(ex) };
        }

        report.Remove("inProgress");
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
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex is LockingExceptionFailedLockAttempt locking)
        {
            o["failedToLock"] = new JArray(locking.GetAllFailed2LockAsString());
        }

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
}
