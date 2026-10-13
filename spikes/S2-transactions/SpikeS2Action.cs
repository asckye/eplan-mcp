using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S2;

/// <summary>
/// Spike S2 (transactions). Answers: which API pattern actually undoes a failed or abandoned change?
/// S0 found that an uncommitted <see cref="SafetyPoint"/> did NOT roll back a created page.
/// Each probe writes something identifiable, abandons it in one specific way, and checks whether it
/// survived. Runs only against a project under ScratchRoot and removes everything it created.
/// <para>
/// Usage: <c>EplanMcpSpikeS2 /Mode:probes|prepareUndo|check|cleanup /Report:"…\x.json" /ScratchRoot:"…\scratch"</c>.
/// prepareUndo + an external XEsUndoAction + check measures whether the engineer's Ctrl+Z undoes our change.
/// </para>
/// </summary>
public sealed class SpikeS2Action : IEplAction
{
    public const string ActionName = "EplanMcpSpikeS2";

    private const string Location = "MCPS2";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        // An assembly loaded into EPLAN cannot be unloaded until EPLAN exits, so re-registering a rebuilt
        // DLL under the same name keeps running the OLD code. Deriving the action name from the assembly
        // name lets a rebuild with -p:AssemblyName=EplanMcp.Spikes.S2r2 load side by side as EplanMcpSpikeS2r2.
        string assemblyName = typeof(SpikeS2Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S2";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string reportPath = GetParameter(oActionCallingContext, "Report");
        string scratchRoot = GetParameter(oActionCallingContext, "ScratchRoot");
        string mode = GetParameter(oActionCallingContext, "Mode");
        string counterText = GetParameter(oActionCallingContext, "Counter");
        string undoMode = GetParameter(oActionCallingContext, "Undo");
        var report = new JObject
        {
            ["spike"] = "S2-transactions",
            ["mode"] = mode.Length == 0 ? "probes" : mode,
            ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        };

        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                Run(report, scratchRoot, mode, counterText, undoMode);
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = DescribeException(ex);
        }

        if (!string.IsNullOrEmpty(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
        }

        return true;
    }

    private static void Run(JObject report, string scratchRoot, string mode, string counterText, string undoMode)
    {
        Project? project = new SelectionSet().GetCurrentProject(false);
        if (project is null)
        {
            report["refused"] = "no project open";
            return;
        }

        string root = Path.GetFullPath(scratchRoot).TrimEnd('\\') + "\\";
        if (!Path.GetFullPath(project.ProjectFullName).StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            report["refused"] = "project is outside the scratch root";
            return;
        }

        report["project"] = project.ProjectName;
        report["pagesInitial"] = project.Pages.Length;

        switch (mode)
        {
            case "prepareUndo":
                Probe(report, "prepare_committed_page_with_undo_step", () => CreateCommittedWithUndo(project, 41));
                return;
            case "op":
                int counter = int.Parse(counterText, CultureInfo.InvariantCulture);
                Probe(report, "op", () => Op(project, counter, undoMode));
                report["spikePages"] = new JArray(SpikePages(project).Select(page => page.Name));
                return;
            case "check":
                report["spikePages"] = new JArray(SpikePages(project).Select(page => page.Name));
                return;
            case "cleanup":
                Probe(report, "cleanup", () => RemoveSpikePages(project));
                report["pagesFinal"] = project.Pages.Length;
                return;
        }
        report["context"] = DescribeTransactionState(project);

        // --- New objects abandoned in different ways (page counters 11..18) ---
        Probe(report, "sp_dispose_without_commit", () => CreateAndAbandon(project, 11, (_, _) => { }));
        Probe(report, "sp_explicit_rollback", () => CreateAndAbandon(project, 12, (sp, _) => sp.Rollback()));
        Probe(report, "sp_start_then_dispose", () => CreateAndAbandon(project, 13, (_, _) => { }, start: true));
        Probe(report, "sp_start_then_rollback", () => CreateAndAbandon(project, 14, (sp, _) => sp.Rollback(), start: true));
        Probe(report, "sp_exception_mid_operation", () => CreateThenThrow(project, 15));
        Probe(report, "tx_abort", () => CreateInTransaction(project, 16, abort: true, projectScoped: false));
        Probe(report, "tx_dispose_without_commit", () => CreateInTransaction(project, 17, abort: false, projectScoped: false));
        Probe(report, "tx_project_scoped_abort", () => CreateInTransaction(project, 18, abort: true, projectScoped: true));

        // --- UndoStep combined with rollback (S0 used an UndoStep around the SafetyPoint) ---
        Probe(report, "undo_outside_sp_dispose", () => UndoAround(project, 51, sp => { }));
        Probe(report, "undo_outside_sp_rollback", () => UndoAround(project, 52, sp => sp.Rollback()));
        Probe(report, "undo_inside_sp_dispose", () => UndoInside(project, 53));
        Probe(report, "undo_outside_tx_abort", () => UndoAroundTransaction(project, 54));

        // --- Modification of an existing object (page counter 21 is the committed fixture) ---
        Page? fixture = null;
        Probe(report, "fixture_committed_page", () =>
        {
            fixture = CreateCommittedPage(project, 21, out JObject info);
            return info;
        });
        if (fixture is not null)
        {
            Page page = fixture;
            Probe(report, "modify_sp_rollback", () => ModifyAndAbandon(page, "S2-sp-rollback", useTransaction: false));
            Probe(report, "modify_tx_abort", () => ModifyAndAbandon(page, "S2-tx-abort", useTransaction: true));
        }

        // --- Undo of a committed change ---
        Probe(report, "undo_step_doundo", () => CreateCommittedThenUndo(project, 31, UndoVariant.CloseThenDoUndo));
        Probe(report, "undo_step_doundo_before_close", () => CreateCommittedThenUndo(project, 32, UndoVariant.DoUndoBeforeClose));
        Probe(report, "undo_step_closeopen_doundo", () => CreateCommittedThenUndo(project, 33, UndoVariant.CloseOpenThenDoUndo));
        Probe(report, "undo_step_doundo_noredo", () => CreateCommittedThenUndo(project, 34, UndoVariant.CloseThenDoUndoNoRedo));

        Probe(report, "cleanup", () => RemoveSpikePages(project));
        report["pagesFinal"] = project.Pages.Length;
    }

    private static JObject DescribeTransactionState(Project project)
    {
        var manager = new TransactionManager();
        var projectManager = new TransactionManager(project);
        return new JObject
        {
            ["safetyPointAutoLocking"] = SafetyPoint.GetAutoLockingState(),
            ["isEplanTransactionRunning"] = manager.IsEplanTransactionRunning,
            ["isTransactionRunning"] = manager.IsTransactionRunning,
            ["project.isEplanTransactionRunning"] = projectManager.IsEplanTransactionRunning,
            ["project.isTransactionRunning"] = projectManager.IsTransactionRunning,
        };
    }

    private static Page NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_LOCATION = Location;
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
        return page;
    }

    private static JObject CreateAndAbandon(Project project, int counter, Action<SafetyPoint, Page> abandon, bool start = false)
    {
        string name;
        JObject during;
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            if (start)
            {
                safetyPoint.Start();
            }

            Page page = NewPage(project, counter);
            name = page.Name;
            during = DescribeTransactionState(project);
            abandon(safetyPoint, page);
        }

        return Outcome(project, name, during);
    }

    private static JObject CreateThenThrow(Project project, int counter)
    {
        string name = string.Empty;
        string caught = string.Empty;
        try
        {
            using SafetyPoint safetyPoint = SafetyPoint.Create();
            name = NewPage(project, counter).Name;
            throw new InvalidOperationException("simulated failure after the write");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex.Message;
        }

        JObject outcome = Outcome(project, name, null);
        outcome["caught"] = caught;
        return outcome;
    }

    private static JObject CreateInTransaction(Project project, int counter, bool abort, bool projectScoped)
    {
        string name;
        JObject during;
        TransactionManager manager = projectScoped ? new TransactionManager(project) : new TransactionManager();
        using (Transaction transaction = manager.CreateTransaction())
        {
            name = NewPage(project, counter).Name;
            during = DescribeTransactionState(project);
            during["implicitEplanTransactionCommitted"] = transaction.IsImplicitEplanTransactionCommited;
            if (abort)
            {
                transaction.Abort();
            }
        }

        return Outcome(project, name, during);
    }

    private static Page CreateCommittedPage(Project project, int counter, out JObject info)
    {
        Page page;
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            page = NewPage(project, counter);
            safetyPoint.Commit();
        }

        info = new JObject { ["name"] = page.Name, ["description"] = ReadDescription(page) };
        return page;
    }

    private static JObject ModifyAndAbandon(Page page, string text, bool useTransaction)
    {
        string before = ReadDescription(page);
        var value = new MultiLangString();
        value.AddString(ISOCode.Language.L___, text);
        string during;

        if (useTransaction)
        {
            using Transaction transaction = new TransactionManager().CreateTransaction();
            page.Properties.PAGE_NOMINATIOMN.Set(value);
            during = ReadDescription(page);
            transaction.Abort();
        }
        else
        {
            using SafetyPoint safetyPoint = SafetyPoint.Create();
            page.Properties.PAGE_NOMINATIOMN.Set(value);
            during = ReadDescription(page);
            safetyPoint.Rollback();
        }

        string after = ReadDescription(page);
        return new JObject
        {
            ["before"] = before,
            ["during"] = during,
            ["after"] = after,
            ["rolledBack"] = after == before,
        };
    }

    private enum UndoVariant
    {
        CloseThenDoUndo,
        DoUndoBeforeClose,
        CloseOpenThenDoUndo,
        CloseThenDoUndoNoRedo,
    }

    private static JObject CreateCommittedThenUndo(Project project, int counter, UndoVariant variant)
    {
        string name;
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        undo.SetUndoDescription("EplanMcp spike S2");
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            name = NewPage(project, counter).Name;
            safetyPoint.Commit();
        }

        bool existsBeforeUndo = Exists(project, name);
        switch (variant)
        {
            case UndoVariant.CloseThenDoUndo:
                undo.CloseUndo();
                undo.DoUndo();
                break;
            case UndoVariant.DoUndoBeforeClose:
                undo.DoUndo();
                undo.CloseUndo();
                break;
            case UndoVariant.CloseOpenThenDoUndo:
                undo.CloseOpenUndo();
                undo.DoUndo();
                break;
            case UndoVariant.CloseThenDoUndoNoRedo:
                undo.CloseUndo();
                undo.DoUndo(true);
                break;
        }

        bool existsAfterUndo = Exists(project, name);
        return new JObject
        {
            ["created"] = name,
            ["existsBeforeUndo"] = existsBeforeUndo,
            ["existsAfterUndo"] = existsAfterUndo,
            ["undoRemovedIt"] = existsBeforeUndo && !existsAfterUndo,
        };
    }

    private static JObject CreateCommittedWithUndo(Project project, int counter)
    {
        string name;
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        undo.SetUndoDescription("EplanMcp spike S2: create page");
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            name = NewPage(project, counter).Name;
            safetyPoint.Commit();
        }

        undo.CloseUndo();
        return new JObject { ["created"] = name, ["exists"] = Exists(project, name) };
    }

    private static JObject UndoAround(Project project, int counter, Action<SafetyPoint> abandon)
    {
        string name;
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        undo.SetUndoDescription("EplanMcp spike S2");
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            name = NewPage(project, counter).Name;
            abandon(safetyPoint);
        }

        bool survivedBeforeCloseUndo = Exists(project, name);
        undo.CloseUndo();
        JObject outcome = Outcome(project, name, null);
        outcome["survivedBeforeCloseUndo"] = survivedBeforeCloseUndo;
        return outcome;
    }

    private static JObject UndoInside(Project project, int counter)
    {
        string name;
        using var undoManager = new UndoManager();
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            UndoStep undo = undoManager.CreateUndoStep();
            undo.SetUndoDescription("EplanMcp spike S2");
            name = NewPage(project, counter).Name;
            undo.CloseUndo();
        }

        return Outcome(project, name, null);
    }

    private static JObject UndoAroundTransaction(Project project, int counter)
    {
        string name;
        using var undoManager = new UndoManager();
        UndoStep undo = undoManager.CreateUndoStep();
        undo.SetUndoDescription("EplanMcp spike S2");
        using (Transaction transaction = new TransactionManager().CreateTransaction())
        {
            name = NewPage(project, counter).Name;
            transaction.Abort();
        }

        undo.CloseUndo();
        return Outcome(project, name, null);
    }

    /// <summary>One committed change, wrapped the way <paramref name="undoMode"/> says, as a separate action call.</summary>
    private static JObject Op(Project project, int counter, string undoMode)
    {
        string name;
        using var undoManager = new UndoManager();
        switch (undoMode)
        {
            case "outside":
            {
                UndoStep undo = undoManager.CreateUndoStep();
                undo.SetUndoDescription("EplanMcp S2 op outside " + counter.ToString(CultureInfo.InvariantCulture));
                using (SafetyPoint safetyPoint = SafetyPoint.Create())
                {
                    name = NewPage(project, counter).Name;
                    safetyPoint.Commit();
                }

                undo.CloseUndo();
                break;
            }

            case "insideSp":
            {
                using SafetyPoint safetyPoint = SafetyPoint.Create();
                UndoStep undo = undoManager.CreateUndoStep();
                undo.SetUndoDescription("EplanMcp S2 op insideSp " + counter.ToString(CultureInfo.InvariantCulture));
                name = NewPage(project, counter).Name;
                safetyPoint.Commit();
                undo.CloseUndo();
                break;
            }

            case "tx":
            {
                UndoStep undo = undoManager.CreateUndoStep();
                undo.SetUndoDescription("EplanMcp S2 op tx " + counter.ToString(CultureInfo.InvariantCulture));
                using (Transaction transaction = new TransactionManager().CreateTransaction())
                {
                    name = NewPage(project, counter).Name;
                    transaction.Commit();
                }

                undo.CloseUndo();
                break;
            }

            case "none":
            {
                using SafetyPoint safetyPoint = SafetyPoint.Create();
                name = NewPage(project, counter).Name;
                safetyPoint.Commit();
                break;
            }

            default:
                throw new ArgumentException("unknown /Undo mode: " + undoMode);
        }

        return new JObject { ["created"] = name, ["undo"] = undoMode, ["exists"] = Exists(project, name) };
    }

    private static string ReadDescription(Page page)
    {
        PropertyValue value = page.Properties.PAGE_NOMINATIOMN;
        return value.IsEmpty ? "<empty>" : value.ToString();
    }

    private static Page[] SpikePages(Project project) =>
        project.Pages.Where(p => p.Name.IndexOf("+" + Location + "/", StringComparison.Ordinal) >= 0).ToArray();

    private static JObject Outcome(Project project, string name, JObject? during)
    {
        bool survived = Exists(project, name);
        var o = new JObject
        {
            ["created"] = name,
            ["survived"] = survived,
            ["rolledBack"] = !survived,
        };
        if (during is not null)
        {
            o["stateDuring"] = during;
        }

        return o;
    }

    private static bool Exists(Project project, string name) => project.Pages.Any(page => page.Name == name);

    private static JObject RemoveSpikePages(Project project)
    {
        var removed = new JArray();
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            foreach (Page page in SpikePages(project))
            {
                removed.Add(page.Name);
                page.Remove();
            }

            safetyPoint.Commit();
        }

        return new JObject { ["removed"] = removed };
    }

    private static void Probe(JObject report, string name, Func<JToken> probe)
    {
        var watch = Stopwatch.StartNew();
        JObject entry;
        try
        {
            entry = new JObject { ["ok"] = true, ["result"] = probe() };
        }
        catch (Exception ex)
        {
            entry = new JObject { ["ok"] = false, ["error"] = DescribeException(ex) };
        }

        entry["ms"] = watch.ElapsedMilliseconds;
        report[name] = entry;
    }

    private static JObject DescribeException(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex.InnerException is not null)
        {
            o["inner"] = DescribeException(ex.InnerException);
        }

        return o;
    }

    private static string GetParameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }
}
