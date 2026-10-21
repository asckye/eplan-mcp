using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RenumberEnums = Eplan.EplApi.HEServices.Renumber.Enums;

namespace EplanMcp.Spikes.S21;

/// <summary>
/// Spike S21 (M4c design 4.4, 4.9): reading cables, terminal strips, terminals, PLC boxes and connections, and the
/// numbering of terminals, cables and connections plus cable generation, run the way the Bridge runs a tool (main
/// thread through the dispatcher, after the scheduling action has returned, writes in SafetyPoint + UndoStep +
/// QuietModeStep), as S11 did. Also lists the licence options the M4c tools may need.
/// <para>
/// Usage: <c>EplanMcpSpikeS21 /Mode:projects|select|restore|license|inventory|names|terminals|terminalsrollback|cables|cablesrollback|wires|wiresrollback|generaterollback|close
/// /Report:"…\x.json" /ScratchRoot:"…\s21" [/Scheme:name] [/Restore:name]</c>. Works only on an open project under
/// ScratchRoot, found by path.
/// </para>
/// </summary>
public sealed class SpikeS21Action : IEplAction
{
    private static readonly string[] _licenceWords = ["plan", "transl", "revis", "cable", "terminal", "plc", "label", "pxf", "pdf", "import", "export", "part", "wire", "dxf", "macro", "langu"];

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS21Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S21";
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
            Scheme = Parameter(oActionCallingContext, "Scheme"),
            Restore = Parameter(oActionCallingContext, "Restore"),
        };
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var thread = new Thread(() => RunLater(dispatcher, p)) { IsBackground = true };
        thread.Start();
        return true;
    }

    private static void RunLater(Dispatcher dispatcher, Params p)
    {
        var report = new JObject { ["spike"] = "S21-cables-terminals", ["mode"] = p.Mode, ["scheme"] = p.Scheme, ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
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

        if (p.Mode == "license")
        {
            report["license"] = Licence();
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
                report["current"] = Try(() => new SelectionSet().GetCurrentProject(false)?.ProjectName);
                break;
            case "inventory":
                report["inventory"] = Timed(() => Inventory(project));
                break;
            case "names":
                report["names"] = Names(project);
                break;
            case "terminals":
                report["terminals"] = Scoped("S21 terminals", () => Changed(project, () => NumberTerminals(project, p.Scheme)));
                break;
            case "terminalsrollback":
                report["terminals"] = RolledBack(project, () => NumberTerminals(project, p.Scheme));
                break;
            case "cables":
                report["cables"] = Scoped("S21 cables", () => Changed(project, () => NumberCables(project, p.Scheme)));
                break;
            case "cablesrollback":
                report["cables"] = RolledBack(project, () => NumberCables(project, p.Scheme));
                break;
            case "wires":
                report["wires"] = Scoped("S21 wires", () => Changed(project, () => DesignateWires(project, p.Scheme)));
                break;
            case "wiresrollback":
                report["wires"] = RolledBack(project, () => DesignateWires(project, p.Scheme));
                break;
            case "generaterollback":
                report["generate"] = RolledBack(project, () => new Generate().Cables(project, p.Scheme, p.Scheme, p.Scheme, false, true, 1, 1, true));
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

    private static JObject Licence()
    {
        var modules = new Dictionary<LicenseOptions, bool>();
        using var licence = new License();
        licence.GetLicenseModules(ref modules);
        var matching = new JObject();
        foreach (KeyValuePair<LicenseOptions, bool> module in modules.Where(m => !m.Key.ToString().StartsWith("APIRuntimeApplication", StringComparison.Ordinal)).OrderBy(m => m.Key.ToString(), StringComparer.Ordinal))
        {
            string name = module.Key.ToString();
            if (_licenceWords.Any(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                matching[name] = module.Value;
            }
        }

        return new JObject { ["options"] = modules.Count, ["available"] = modules.Count(m => m.Value), ["matching"] = matching };
    }

    private static JObject Inventory(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var result = new JObject();

        // Cables: the XML says null gives the elements a GUI navigator shows without a filter, and also that null throws.
        result["cablesWithNullScheme"] = Try(() => finder.GetCablesWithFilterScheme(null).Length);
        result["cablesWithEmptyScheme"] = Try(() => finder.GetCablesWithFilterScheme(string.Empty).Length);
        Function[] byCategory = Try2(() => ByCategory(finder, FunctionCategory.Cable)) ?? [];
        result["cablesByCategory"] = byCategory.Length;
        result["cableSamples"] = new JArray(byCategory.Take(5).Select(f => Try(() => CableSample(f))));

        TerminalStrip[] strips = Try2(() => finder.GetTerminalStrips(null)) ?? [];
        result["terminalStrips"] = strips.Length;
        result["stripSamples"] = new JArray(strips.Take(5).Select(s => Try(() => StripSample(s))));

        Terminal[] terminals = Try2(() => finder.GetTerminals(null)) ?? [];
        result["terminals"] = terminals.Length;
        result["terminalsWithQuestionMark"] = terminals.Count(t => (t.Name ?? string.Empty).Contains('?'));
        result["mainTerminals"] = terminals.Count(t => Try3(() => t.IsMainTerminal));
        result["terminalSamples"] = new JArray(terminals.Take(5).Select(t => Try(() => TerminalSample(t))));

        PLC[] plcs = Try2(() => finder.GetPLCs(null)) ?? [];
        result["plcs"] = plcs.Length;
        result["plcSamples"] = new JArray(plcs.Take(8).Select(c => Try(() => PlcSample(c))));

        result["plugs"] = Try(() => finder.GetPlugs(null).Length);
        result["plugStrips"] = Try(() => finder.GetPlugStrips(null).Length);
        result["cableUnits"] = Try(() => finder.GetCableUnits(null).Length);

        Connection[] connections = Try2(() => finder.GetConnections(null)) ?? [];
        result["connections"] = connections.Length;
        result["connectionsWithDesignation"] = connections.Count(c => Text(c.Properties.CONNECTION_DESIGNATION).Length > 0);
        result["connectionSamples"] = new JArray(connections.Where(c => Text(c.Properties.CONNECTION_DESIGNATION).Length > 0).Take(5).Select(c => Try(() => new JObject
        {
            ["designation"] = Text(c.Properties.CONNECTION_DESIGNATION),
            ["start"] = Try(() => c.StartPin is { } s ? s.ParentFunction?.Name + ":" + s.Name : null),
            ["end"] = Try(() => c.EndPin is { } e ? e.ParentFunction?.Name + ":" + e.Name : null),
            ["page"] = Try(() => c.Page?.Name),
        })));

        result["comparisons"] = Try(() => project.Comparisons?.Length);
        result["uncompletedPages"] = Try(() => new Revision().GetUncompletedPages(project)?.Length);
        return result;
    }

    private static Function[] ByCategory(DMObjectsFinder finder, FunctionCategory category)
    {
        using var filter = new FunctionsFilter();
        filter.FunctionCategory = category;
        return finder.GetFunctions(filter);
    }

    private static JObject CableSample(Function function)
    {
        var sample = new JObject
        {
            ["name"] = function.Name,
            ["type"] = function.GetType().Name,
            ["category"] = function.FunctionCategory.ToString(),
            ["page"] = function.Page?.Name,
            ["isMain"] = function.IsMainFunction,
            ["parts"] = new JArray(function.ArticleReferences.Select(a => a.PartNr)),
        };
        if (function is Cable cable)
        {
            sample["cableConnections"] = Try(() => cable.CableConnections?.Length);
            var sources = new List<StorableObject>();
            var targets = new List<StorableObject>();
            sample["sourcesAndTargets"] = Try(() =>
            {
                cable.GetSourcesAndTargets(sources, targets);
                return new JObject
                {
                    ["sources"] = new JArray(sources.Take(4).Select(Describe)),
                    ["targets"] = new JArray(targets.Take(4).Select(Describe)),
                    ["sourceCount"] = sources.Count,
                    ["targetCount"] = targets.Count,
                };
            });
        }

        return sample;
    }

    private static JObject StripSample(TerminalStrip strip) => new()
    {
        ["name"] = strip.Name,
        ["page"] = strip.Page?.Name,
        ["terminals"] = Try(() => strip.Terminals?.Length),
        ["firstTerminals"] = Try(() => new JArray((strip.Terminals ?? []).Take(4).Select(t => t.Name))),
        ["bridges"] = Try(() => strip.Bridges?.Length),
    };

    private static JObject TerminalSample(Terminal terminal) => new()
    {
        ["name"] = terminal.Name,
        ["page"] = terminal.Page?.Name,
        ["isMain"] = Try(() => terminal.IsMainTerminal),
        ["strip"] = Try(() => terminal.TerminalStrip?.Name),
        ["external"] = Try(() => terminal.ExternalConnections?.Length),
        ["internal"] = Try(() => terminal.InternalConnections?.Length),
        ["plc"] = Try(() => terminal.PLC?.Name),
    };

    private static JObject PlcSample(PLC plc) => new()
    {
        ["name"] = plc.Name,
        ["page"] = plc.Page?.Name,
        ["isMain"] = plc.IsMainFunction,
        ["isCpu"] = Try(() => plc.IsCPU),
        ["scheme"] = Try(() => plc.Scheme),
        ["plcTerminals"] = Try(() => plc.PLCTerminals?.Length),
        ["firstTerminals"] = Try(() => new JArray((plc.PLCTerminals ?? []).Take(4).Select(t => new JObject { ["name"] = t.Name, ["io"] = Try(() => t.PlcIOEntry?.ToString()) }))),
        ["parts"] = new JArray(plc.ArticleReferences.Select(a => a.PartNr)),
    };

    private static void NumberTerminals(Project project, string scheme)
    {
        using var finder = new DMObjectsFinder(project);
        Function[] terminals = [.. finder.GetTerminals(null).Cast<Function>()];
        new Renumber().Terminals(
            terminals,
            RenumberEnums.TerminalsSequence.Level,
            RenumberEnums.Extent.AllSelectedTerminalStrips,
            RenumberEnums.TerminalsPotential.Numbering,
            RenumberEnums.TerminalsPotential.Numbering,
            RenumberEnums.TerminalsPotential.Numbering,
            RenumberEnums.MultipleTerminals.NumberIndividually,
            RenumberEnums.TerminalsWithAlphabeticalCharacters.DontModify,
            scheme,
            1,
            1,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false);
    }

    private static void NumberCables(Project project, string scheme)
    {
        using var finder = new DMObjectsFinder(project);
        Function[] cables = ByCategory(finder, FunctionCategory.Cable);
        new CableService().RenumberCables(cables, scheme, false, true, 1, 1);
    }

    private static void DesignateWires(Project project, string scheme) =>
        new ConnectionService().DesignateConnections(project, scheme, ConnectionService.DesignateOverwrition.All, false, false);

    /// <summary>Every terminal, cable and connection name, keyed by the object's identifier.</summary>
    private static Dictionary<string, string> NameMap(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Terminal t in finder.GetTerminals(null))
        {
            map["T " + t.ToStringIdentifier()] = t.Name ?? string.Empty;
        }

        foreach (Function c in ByCategory(finder, FunctionCategory.Cable))
        {
            map["C " + c.ToStringIdentifier()] = c.Name ?? string.Empty;
        }

        foreach (Connection w in finder.GetConnections(null))
        {
            map["W " + w.ToStringIdentifier()] = Text(w.Properties.CONNECTION_DESIGNATION);
        }

        return map;
    }

    private static JObject Names(Project project)
    {
        Dictionary<string, string> map = NameMap(project);
        return new JObject
        {
            ["terminals"] = map.Count(m => m.Key.StartsWith("T ", StringComparison.Ordinal)),
            ["cables"] = map.Count(m => m.Key.StartsWith("C ", StringComparison.Ordinal)),
            ["connections"] = map.Count(m => m.Key.StartsWith("W ", StringComparison.Ordinal)),
            ["designatedConnections"] = map.Count(m => m.Key.StartsWith("W ", StringComparison.Ordinal) && m.Value.Length > 0),
            ["digest"] = Digest(map),
        };
    }

    private static string Digest(Dictionary<string, string> map)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        string text = string.Join("\n", map.OrderBy(m => m.Key, StringComparer.Ordinal).Select(m => m.Key + "=" + m.Value));
        return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).Substring(0, 16);
    }

    private static JObject Changed(Project project, System.Action change)
    {
        Dictionary<string, string> before = NameMap(project);
        var clock = Stopwatch.StartNew();
        JToken error = JValue.CreateNull();
        try
        {
            change();
        }
        catch (Exception ex)
        {
            error = Describe(ex);
        }

        double seconds = Math.Round(clock.Elapsed.TotalSeconds, 2);
        Dictionary<string, string> after = NameMap(project);
        string[] changed = [.. after.Where(a => !before.TryGetValue(a.Key, out string? old) || old != a.Value).Select(a => a.Key)];
        return new JObject
        {
            ["seconds"] = seconds,
            ["error"] = error,
            ["changed"] = changed.Length,
            ["changedByKind"] = new JObject(changed.GroupBy(k => k.Substring(0, 1)).Select(g => new JProperty(g.Key, g.Count()))),
            ["samples"] = new JArray(changed.Take(12).Select(k => (before.TryGetValue(k, out string? old) ? old : "(new)") + " -> " + after[k])),
            ["added"] = after.Keys.Count(k => !before.ContainsKey(k)),
            ["removed"] = before.Keys.Count(k => !after.ContainsKey(k)),
            ["digestBefore"] = Digest(before),
            ["digestAfter"] = Digest(after),
        };
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

    private static JToken RolledBack(Project project, System.Action change)
    {
        JObject result;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            result = Changed(project, change);
            safety.Rollback();
        }

        result["afterRollback"] = Names(project);
        return result;
    }

    private static JToken Timed(Func<JObject> work)
    {
        var clock = Stopwatch.StartNew();
        JObject result = work();
        result["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        return result;
    }

    private static string Text(PropertyValue value)
    {
        try
        {
            return value.IsEmpty ? string.Empty : value.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
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

    private static JToken Describe(StorableObject o) => Try(() => o switch
    {
        Function f => new JObject { ["kind"] = f.GetType().Name, ["name"] = f.Name },
        Connection c => new JObject { ["kind"] = "Connection", ["designation"] = Text(c.Properties.CONNECTION_DESIGNATION) },
        _ => new JObject { ["kind"] = o.GetType().Name },
    });

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

    private static T? Try2<T>(Func<T> probe)
        where T : class
    {
        try
        {
            return probe();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Try3(Func<bool> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception)
        {
            return false;
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

        public string Scheme { get; set; } = string.Empty;

        public string Restore { get; set; } = string.Empty;
    }
}
