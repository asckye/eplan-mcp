using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S15;

/// <summary>
/// The S15 probes, in order (M5 design 4.9):
/// <list type="number">
///   <item>Init without the licence and login dialogs: does it succeed under the developer licence, how long it
///   takes, and what licence and variant it reports.</item>
///   <item>Reading pages outside a LockingStep.</item>
///   <item>A page created in a SafetyPoint and rolled back.</item>
///   <item>A page created in the Bridge's transaction scope (SafetyPoint, the UndoStep inside it, commit), then
///   <c>UndoStep.DoUndo</c>; there is no Ctrl+Z without a user interface.</item>
///   <item>A page created with the UndoStep outside the SafetyPoint and rolled back (A7: rollback silently fails in
///   an add-in).</item>
/// </list>
/// The result file is rewritten after every probe, so a crash leaves what ran before it.
/// </summary>
internal sealed class Probes(string bin, string projectPath, string resultPath, Stopwatch clock)
{
    private const string Plant = "GAA";
    private const string Location = "A1";
    private const string DocType = "EFS1";
    private readonly JObject _result = [];

    public int Run()
    {
        Record("environment", new JObject
        {
            ["process"] = Process.GetCurrentProcess().Id,
            ["session"] = Process.GetCurrentProcess().SessionId,
            ["userInteractive"] = Environment.UserInteractive,
            ["apartment"] = Thread.CurrentThread.GetApartmentState().ToString(),
            ["is64BitProcess"] = Environment.Is64BitProcess,
            ["secondsBeforeInit"] = Seconds(),
        });

        var app = new EplApplication { EplanBinFolder = bin };
        double started = Seconds();
        try
        {
            app.Init(string.Empty, false, false);
        }
        catch (Exception ex)
        {
            Record("init", Failure(ex, Seconds() - started));
            return 1;
        }

        Record("init", new JObject
        {
            ["seconds"] = Math.Round(Seconds() - started, 2),
            ["variant"] = Try(() => app.Variant),
            ["license"] = Try(() => app.License),
            ["version"] = Try(() => app.Version),
            ["inOfflineMode"] = Try(() => EplApplication.InOfflineMode.ToString()),
            ["quietMode"] = Try(() => app.QuietMode.ToString()),
            ["culture"] = CultureInfo.CurrentCulture.Name,
        });

        // HasOption and GetLicenseModules only check; LockOption would hold a licence (api-notes A2).
        Step("licence", () =>
        {
            using var license = new License();
            var modules = new Dictionary<LicenseOptions, bool>();
            license.GetLicenseModules(ref modules);
            return (0, new JObject
            {
                ["apiExtension"] = license.HasOption(LicenseOptions.APIExtension),
                ["electricP8"] = license.HasOption(LicenseOptions.ElectricP8),
                ["options"] = modules.Count,
                ["available"] = modules.Count(m => m.Value),
            });
        });

        try
        {
            Project project = Step("open", () =>
            {
                using (new LockingStep())
                {
                    using var manager = new ProjectManager();
                    Project opened = manager.OpenProject(projectPath);
                    return (opened, new JObject
                    {
                        ["name"] = opened.ProjectName,
                        ["pages"] = Pages(opened).Length,
                        // Pages an earlier run on the same copy left: whether its committed changes were saved.
                        ["earlier"] = new JArray(new[] { 9101, 9102, 9103 }.Where(c => Has(opened, c))),
                    });
                }
            });

            Step("readWithoutLockingStep", () => (0, new JObject { ["pages"] = Pages(project).Length }));
            using (new LockingStep())
            {
                Step("rollback", () => RolledBack(project, 9101, undoFirst: false));
                Step("commitAndDoUndo", () => CommittedThenUndone(project, 9102));
                Step("rollbackWithUndoStepOutside", () => RolledBack(project, 9103, undoFirst: true));
                Step("close", () =>
                {
                    project.Close();
                    return (0, new JObject());
                });
            }
        }
        finally
        {
            double exiting = Seconds();
            app.Exit();
            Record("exit", new JObject { ["seconds"] = Math.Round(Seconds() - exiting, 2), ["totalSeconds"] = Math.Round(Seconds(), 2) });
        }

        return 0;
    }

    private static (int, JObject) RolledBack(Project project, int counter, bool undoFirst)
    {
        int before = Pages(project).Length;
        using var undoManager = new UndoManager();
        UndoStep? outside = undoFirst ? undoManager.CreateUndoStep() : null;
        int inside;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            NewPage(project, counter);
            inside = Pages(project).Length;
            safety.Rollback();
        }

        outside?.CloseUndo();
        return (0, new JObject { ["before"] = before, ["inside"] = inside, ["after"] = Pages(project).Length, ["leftOver"] = Has(project, counter) });
    }

    private static (int, JObject) CommittedThenUndone(Project project, int counter)
    {
        int before = Pages(project).Length;
        using var undoManager = new UndoManager();
        UndoStep undo;
        using (SafetyPoint safety = SafetyPoint.Create())
        {
            undo = undoManager.CreateUndoStep();
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                NewPage(project, counter);
            }

            safety.Commit();
            undo.SetUndoDescription("AI: S15 offline probe");
            undo.CloseUndo();
        }

        int committed = Pages(project).Length;
        var report = new JObject { ["before"] = before, ["committed"] = committed };
        try
        {
            undo.DoUndo();
            report["afterDoUndo"] = Pages(project).Length;
            report["leftOver"] = Has(project, counter);
        }
        catch (Exception ex)
        {
            report["doUndo"] = Failure(ex, 0);
        }

        return (0, report);
    }

    private static void NewPage(Project project, int counter)
    {
        using var properties = new PagePropertyList();
        properties.DESIGNATION_PLANT = Plant;
        properties.DESIGNATION_LOCATION = Location;
        properties.DESIGNATION_DOCTYPE = DocType;
        properties.PAGE_COUNTER = counter;
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
    }

    private static bool Has(Project project, int counter) =>
        Array.Exists(Pages(project), p => p.Name == $"={Plant}+{Location}&{DocType}/{counter.ToString(CultureInfo.InvariantCulture)}");

    private static Page[] Pages(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null);
    }

    private T Step<T>(string name, Func<(T Value, JObject Report)> probe)
    {
        double started = Seconds();
        try
        {
            (T value, JObject report) = probe();
            report["seconds"] = Math.Round(Seconds() - started, 2);
            Record(name, report);
            return value;
        }
        catch (Exception ex)
        {
            Record(name, Failure(ex, Seconds() - started));
            if (name == "open")
            {
                throw;
            }

            return default!;
        }
    }

    private static JObject Failure(Exception ex, double seconds) => new()
    {
        ["failed"] = true,
        ["seconds"] = Math.Round(seconds, 2),
        ["type"] = ex.GetType().FullName,
        ["hresult"] = ex.HResult,
        ["message"] = ex.Message,
    };

    private static string? Try(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return "(" + ex.GetType().Name + ": " + ex.Message + ")";
        }
    }

    private double Seconds() => clock.Elapsed.TotalSeconds;

    private void Record(string name, JObject report)
    {
        _result[name] = report;
        File.WriteAllText(resultPath, _result.ToString(Formatting.Indented));
        Console.Error.WriteLine($"{Seconds(),7:0.00}s {name}: {report.ToString(Formatting.None)}");
    }
}
