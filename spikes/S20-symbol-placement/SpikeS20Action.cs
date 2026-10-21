using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S20;

/// <summary>
/// Spike S20 (M4c design 4.3): placing one device without a window macro, run the way the Bridge runs a tool (main
/// thread through the dispatcher, after the scheduling action has returned). S10 found that a function created from a
/// library symbol shows no device tag and that selecting a property arrangement does not help; this tries
/// <c>PropertyPlacement.Create</c> for the tag, and <c>DeviceService.CreateDevice</c>, which places a part's macro.
/// <para>
/// Usage: <c>EplanMcpSpikeS20 /Mode:projects|select|restore|pages|symbol|devicerollback|device|images|count|close
/// /Report:"…\x.json" /ScratchRoot:"…\s20" [/Out:"…\dir"] [/Restore:name]</c>. Works only on an open project under
/// ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS20Action : IEplAction
{
    private const int SymbolPage = 90;
    private const int DevicePage = 91;
    private const string CoilTag = "=GAA+A1-KF96";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS20Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S20";
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
        var report = new JObject { ["spike"] = "S20-symbol-placement", ["mode"] = p.Mode, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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
            report["restored"] = previous is not null && new Edit().SelectProjectInPagesNavigator(previous);
            return;
        }

        Project? project = open.FirstOrDefault(o => IsUnder(o.ProjectFullName, p.ScratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        switch (p.Mode)
        {
            case "select":
                report["select"] = new Edit().SelectProjectInPagesNavigator(project);
                break;
            case "pages":
                report["pages"] = Scoped("S20 pages", () => new JArray(NewPage(project, SymbolPage).Name, NewPage(project, DevicePage).Name));
                break;
            case "symbol":
                report["symbol"] = PlaceCoil(project, PageNamed(project, SymbolPage));
                break;
            case "devicerollback":
                report["device"] = PlaceDevice(project, PageNamed(project, DevicePage), rollBack: true);
                break;
            case "device":
                report["device"] = PlaceDevice(project, PageNamed(project, DevicePage), rollBack: false);
                break;
            case "images":
                Directory.CreateDirectory(p.Out);
                using (var export = new Export())
                {
                    foreach (int counter in new[] { SymbolPage, DevicePage })
                    {
                        export.GraphicPage(PageNamed(project, counter), p.Out, Export.Format.PNG, 24, 2000, Export.Compression.None, false);
                    }
                }

                report["images"] = new JArray(Directory.EnumerateFiles(p.Out).Select(Path.GetFileName));
                break;
            case "count":
                report["count"] = new JObject
                {
                    ["symbolPage"] = Describe(Functions(project, PageNamed(project, SymbolPage))),
                    ["devicePage"] = Describe(Functions(project, PageNamed(project, DevicePage))),
                    ["coil"] = Functions(project, null).Count(f => f.Name == CoilTag),
                };
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
    /// A coil from the IEC symbol library in the shape S9/S10 left for it (UndoStep around a Transaction, no SafetyPoint),
    /// then a property placement for the device tag, which S10 did not try.
    /// </summary>
    private static JObject PlaceCoil(Project project, Page page)
    {
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        using Transaction transaction = new TransactionManager().CreateTransaction();
        page.LockObject();
        using var library = new SymbolLibrary(project, "IEC_symbol");
        using var symbol = new Symbol(library, "K");
        var function = new Function();
        function.Create(page, symbol.Variants[0]);
        function.Location = new PointD(60, 160);
        function.Name = CoilTag;
        var result = new JObject
        {
            ["name"] = function.Name,
            ["visibleNameBefore"] = function.VisibleName,
            ["placementsBefore"] = Placements(function),
        };
        result["createFullName"] = Try(() =>
        {
            var placement = new PropertyPlacement();
            placement.Create(function, Properties.Function.FUNC_DEVICETAG_FULLNAME);
            return new JObject { ["display"] = placement.GetDisplayString(), ["property"] = placement.DisplayedProperty.ToString() };
        });
        result["visibleNameAfter"] = function.VisibleName;
        result["placementsAfter"] = Placements(function);
        result["localPlacements"] = Try(() => function.UseLocalPropertyPlacements);
        transaction.Commit();
        undo.SetUndoDescription("S20 coil");
        undo.CloseUndo();
        result["namedAfterCommit"] = Functions(project, null).Count(f => f.Name == CoilTag);
        return result;
    }

    /// <summary>
    /// A device from a part with a macro, as a macro is inserted (SafetyPoint + UndoStep, A13). The part is the first one,
    /// in project order, of a coil's main function that has one.
    /// </summary>
    private static JObject PlaceDevice(Project project, Page page, bool rollBack)
    {
        string[] candidates = [.. Functions(project, null)
            .Where(f => f.IsMainFunction && f.FunctionCategory.ToString() == "Coil" && f.ArticleReferences.Length > 0)
            .Select(f => f.ArticleReferences[0].PartNr)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Take(5)];
        var result = new JObject { ["candidates"] = new JArray(candidates), ["before"] = Describe(Functions(project, page)) };
        var attempts = new JArray();
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            using var undoManager = new UndoManager();
            UndoStep undo = undoManager.CreateUndoStep();
            foreach (string part in candidates)
            {
                var clock = Stopwatch.StartNew();
                JToken outcome = Try(() =>
                {
                    Function[] created = new DeviceService().CreateDevice(part, "1", page, new PointD(80, 160));
                    return new JArray(created.Select(Describe));
                });
                attempts.Add(new JObject { ["part"] = part, ["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2), ["result"] = outcome });
                if (outcome is JArray)
                {
                    break;
                }
            }

            result["attempts"] = attempts;
            result["during"] = Describe(Functions(project, page));
            if (rollBack)
            {
                safety.Rollback();
            }
            else
            {
                safety.Commit();
                undo.SetUndoDescription("S20 device");
                undo.CloseUndo();
            }
        }

        result["after"] = Describe(Functions(project, page));
        return result;
    }

    private static JArray Placements(Function function) =>
        new(function.PropertyPlacements.Select(pp => Try(() => new JObject { ["property"] = pp.DisplayedProperty.ToString(), ["display"] = pp.GetDisplayString() })));

    private static JToken Describe(Function f) => Try(() => new JObject
    {
        ["name"] = f.Name,
        ["visibleName"] = f.VisibleName,
        ["main"] = f.IsMainFunction,
        ["category"] = f.FunctionCategory.ToString(),
        ["placed"] = f.IsPlaced,
        ["placements"] = f.PropertyPlacements.Length,
    });

    private static JArray Describe(Function[] functions) => new(functions.Select(Describe));

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

    private static Page PageNamed(Project project, int counter)
    {
        string name = "=GAA+A1&EFS1/" + counter.ToString(CultureInfo.InvariantCulture);
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).FirstOrDefault(pg => pg.Name == name) ?? throw new InvalidOperationException("probe page missing; run 'pages' first: " + name);
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

    /// <summary>The Bridge's TransactionScope (M3 design 4.2): SafetyPoint, the UndoStep inside it, commit, close.</summary>
    private static JToken Scoped(string description, Func<JToken> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        JToken result = change();
        safety.Commit();
        undo.SetUndoDescription(description);
        undo.CloseUndo();
        return result;
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
