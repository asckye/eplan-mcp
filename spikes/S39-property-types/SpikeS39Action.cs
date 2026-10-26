using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S39;

public sealed class SpikeS39Action : IEplAction
{
    private static int _busy;
    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = typeof(SpikeS39Action).Assembly.GetName().Name!.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        bool ownsGate = false;
        try
        {
            string Get(string name) { string value = string.Empty; oActionCallingContext.GetParameter(name, ref value); return value; }
            string report = Path.GetFullPath(Get("Report")), project = Path.GetFullPath(Get("Project")), runId = Get("RunId");
            if (Path.GetExtension(report) != ".json" || runId.Length == 0) { throw new ArgumentException("Report.json and RunId are required."); }
            ownsGate = Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
            if (!ownsGate) { return false; }
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            new Thread(() => RunLater(dispatcher, project, report, runId)) { IsBackground = true }.Start();
            return true;
        }
        catch (Exception ex)
        {
            if (ownsGate) { Interlocked.Exchange(ref _busy, 0); }
            Trace.TraceError("S39 scheduling failed: {0}", ex);
            return false;
        }
    }
    private static void RunLater(Dispatcher dispatcher, string projectPath, string file, string runId)
    {
        var report = new JObject
        {
            ["spike"] = "S39-property-types", ["runId"] = runId,
            ["assembly"] = typeof(SpikeS39Action).Assembly.GetName().Name,
            ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };
        try
        {
            report["sdkFileVersion"] = FileVersionInfo.GetVersionInfo(typeof(PropertyValue).Assembly.Location).FileVersion;
            Thread.Sleep(300); // Let the Remote Client scheduling action return (S32/S34).
            object? done = dispatcher.Invoke(DispatcherPriority.Normal, TimeSpan.FromSeconds(120), new Func<bool>(() =>
            {
                try
                {
                    using var locking = new LockingStep(); // First API operation on the main thread (S1).
                    using Process process = Process.GetCurrentProcess();
                    if (process.MainWindowHandle != IntPtr.Zero && !IsWindowEnabled(process.MainWindowHandle))
                    { throw new InvalidOperationException("Close the EPLAN modal dialog before running S39."); }
                    using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
                    using var manager = new ProjectManager();
                    Project project = manager.OpenProjects.SingleOrDefault(p => string.Equals(p.ProjectLinkFilePath, projectPath, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Open the specified sample fixture before running S39.");
                    report["project"] = project.ProjectLinkFilePath;
                    PropertySamples.Read(project, report);
                }
                catch (Exception ex) { report["fatal"] = ex.GetType().FullName + ": " + ex.Message; } // S1: never escape the delegate.
                return true;
            }));
            if (done is null) { report["fatal"] = "Dispatcher did not start within 120 seconds."; }
        }
        catch (Exception ex) { report["fatal"] = ex.GetType().FullName + ": " + ex.Message; }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            try
            {
                File.WriteAllText(file + ".partial", report.ToString(Formatting.Indented), new UTF8Encoding(false));
                File.Move(file + ".partial", file);
            }
            catch (Exception ex) { Trace.TraceError("S39 report publication failed: {0}", ex); }
        }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
