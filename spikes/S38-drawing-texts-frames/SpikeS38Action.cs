using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38;

/// <summary>S34 scheduling pattern: the action returns before the main-thread probes begin.</summary>
public sealed partial class SpikeS38Action : IEplAction
{
    private static int _busy;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = typeof(SpikeS38Action).Assembly.GetName().Name!.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        var watch = Stopwatch.StartNew();
        string output = string.Empty;
        bool ownsGate = false;
        try
        {
            string requestFile = string.Empty;
            oActionCallingContext.GetParameter("Report", ref output);
            oActionCallingContext.GetParameter("Request", ref requestFile);
            output = Evidence.ReportPath(output);
            requestFile = Evidence.ReportPath(requestFile);
            var request = JObject.Parse(File.ReadAllText(requestFile));
            Evidence.ProjectPath(request.Value<string>("project")!);
            ownsGate = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
            if (!ownsGate) { throw new InvalidOperationException("S38 is already running."); }
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => RunLater(dispatcher, request, output)) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex)
        {
            if (ownsGate) { Interlocked.Exchange(ref _busy, 0); }
            Publish(output, new JObject { ["spike"] = "S38", ["fatal"] = Evidence.Error(ex), ["elapsedMs"] = watch.Elapsed.TotalMilliseconds });
            return false;
        }
    }

    private static void RunLater(Dispatcher dispatcher, JObject request, string output)
    {
        var watch = Stopwatch.StartNew();
        var report = new JObject
        {
            ["spike"] = "S38-drawing-texts-frames", ["revision"] = typeof(SpikeS38Action).Assembly.GetName().Name,
            ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["request"] = request.DeepClone(),
            ["engineerDialogObservation"] = JValue.CreateNull(),
        };
        try
        {
            Thread.Sleep(300);
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                // A9: LockingStep first; no exception may escape this delegate.
                try
                {
                    using var locking = new LockingStep();
                    if (MainWindowDisabled()) { throw new InvalidOperationException("Close EPLAN's modal dialog before running S38."); }
                    using var lists = new PropertyLists();
                    Run(request, report, lists);
                }
                catch (Exception ex) { report["fatal"] = Evidence.Error(ex); }
                return true;
            }));
            if (done is null) { throw new TimeoutException("EPLAN did not start S38 within 120 s."); }
        }
        catch (Exception ex) { report["fatal"] = Evidence.Error(ex); }
        finally
        {
            report["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
            Interlocked.Exchange(ref _busy, 0);
            Publish(output, report);
        }
    }

    private static void Run(JObject request, JObject report, PropertyLists lists)
    {
        string path = Evidence.ProjectPath(request.Value<string>("project")!);
        using var manager = new ProjectManager();
        foreach (Project opened in manager.OpenProjects) { Evidence.ProjectPath(opened.ProjectLinkFilePath); }
        Project project = manager.OpenProjects.SingleOrDefault(p => string.Equals(p.ProjectLinkFilePath, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Open the requested scratch copy in EPLAN first; S38 never opens or closes projects.");
        Evidence.ProjectPath(project.ProjectLinkFilePath);
        string mode = request.Value<string>("mode")!;
        report["project"] = path;
        int bookmark = new SysMessagesCollection().Count;
        JToken before = Evidence.Capture(report, "hashesBefore", () => Evidence.Hashes(path));
        try
        {
            if (before["files"] is not JObject) { throw new InvalidOperationException("Baseline design-file hashing failed; refusing to continue."); }
            Publish(request.Value<string>("checkpoint")!, report);
            if (mode == "findpage") { Evidence.Capture(report, "findpage", () => FindPage(project)); return; }
            string pageName = request.Value<string>("page")!;
            Page page = project.Pages.SingleOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal))
                ?? throw new ArgumentException("No exact full page name matches --page.");
            report["page"] = Identity(page);
            report["page"]!["name"] = page.Name;
            switch (mode)
            {
                case "texts": Evidence.Capture(report, "inventory", () => Inventory(page, lists)); break;
                case "replace": Replace(project, page, lists, report, request, outputCheckpoint: () => Publish(request.Value<string>("checkpoint")!, report)); break;
                case "titleblock": TitleBlock(project, page, lists, report, request); break;
                case "undo-check":
                    RequireUndoTarget(project);
                    VerifyCommitted(page, request, report);
                    report["undoAllowed"] = true;
                    break;
                case "undo-verify": VerifyUndo(page, request, report); break;
                default: throw new ArgumentException("Unknown mode.");
            }
        }
        finally
        {
            JToken after = Evidence.Capture(report, "hashesAfter", () => Evidence.Hashes(path));
            Evidence.Capture(report, "designFileComparison", () => Evidence.CompareHashes(before, after));
            report["hashEvidenceComplete"] = before["files"] is JObject && after["files"] is JObject;
            Evidence.Capture(report, "systemMessages", () => MessagesSince(bookmark));
            report["mainWindowDisabledAfter"] = MainWindowDisabled();
        }
    }

    private static void RequireUndoTarget(Project project)
    {
        using var manager = new ProjectManager();
        Project[] opened = manager.OpenProjects;
        foreach (Project p in opened) { Evidence.ProjectPath(p.ProjectLinkFilePath); }
        var selection = new SelectionSet();
        Project? current = selection.GetCurrentProject(true);
        if (opened.Length != 1 || current is null || !string.Equals(current.ProjectLinkFilePath, project.ProjectLinkFilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("For replace/undo, open only the target scratch copy and make it current (A41/A46). Do not interact until the driver finishes.");
        }
    }

    private static JObject MessagesSince(int bookmark)
    {
        var messages = new SysMessagesCollection(); // A11: never keep two live native message collections.
        var rows = new JArray();
        int index = 0;
        foreach (BaseException message in messages)
        {
            if (index++ >= bookmark) { rows.Add(new JObject { ["level"] = (int)message.MessageLevel, ["text"] = message.GetMessageText() }); }
        }
        return new JObject { ["count"] = rows.Count, ["entries"] = rows };
    }

    private static JObject Identity(StorableObject obj) => new()
    {
        ["type"] = obj.GetType().FullName, ["oid"] = obj.ObjectIdentifier,
        ["sessionIdentifier"] = obj.ToStringIdentifier(),
    };

    private static void Publish(string path, JObject report)
    {
        try { Evidence.Write(path, report); }
        catch (Exception ex) { Trace.WriteLine("S38 report write failed: " + ex); }
    }

    private static bool MainWindowDisabled()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    // A36: keep exactly one list for each object throughout an invocation, including before/after reads.
    private sealed class PropertyLists : IDisposable
    {
        private readonly Dictionary<string, UniversalPropertyList> _lists = new(StringComparer.Ordinal);
        public UniversalPropertyList Get(StorableObject obj)
        {
            string key = obj.ToStringIdentifier();
            if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
            {
                list = obj switch { Project p => p.Properties, Page p => p.Properties, Function f => f.Properties, _ => obj.Properties };
                _lists.Add(key, list);
            }
            return list;
        }
        public void Dispose() { foreach (UniversalPropertyList list in _lists.Values) { list.Dispose(); } }
    }
}
