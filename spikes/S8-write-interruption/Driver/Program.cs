using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S8.Driver;

/// <summary>
/// Drives spike S8 (ADR-0010): ends EPLAN with taskkill /F while the spike add-in's slow write is running, before and
/// after its commit, and once while it triggers the native access violation of api-notes A11; after each, starts
/// EPLAN again with the same scratch copy and counts the probe pages that survived. Only a copy under
/// <c>&lt;scratch&gt;\spikes\s8\</c> is ever open; the EPLAN running at the start (the eval fixture, also scratch) is
/// closed gracefully first. Every EPLAN call has a watchdog: a dialog that EPLAN shows after a restart stops the run
/// and names the window, rather than hanging. Reports go to <c>.local\spikes\s8\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S8.Driver.exe --host name [--revision rN] [--port 49152]</c>.</para>
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private const string Template = @"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project";
    private const string Variant = "Electric P8";

    private static readonly JArray _steps = [];
    private static readonly JArray _dialogs = [];
    private static string _host = string.Empty;
    private static string _port = "49152";
    private static string _action = string.Empty;
    private static string _title = string.Empty;
    private static string _addIn = string.Empty;
    private static string _scratch = string.Empty;
    private static string _project = string.Empty;
    private static string _output = string.Empty;
    private static EplanRemoteClient? _client;

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // EPLAN types load only after the resolver is in place
    private static int Run(string[] args)
    {
        string revision = Option(args, "--revision") ?? string.Empty;
        _port = Option(args, "--port") ?? "49152";
        _host = Option(args, "--host") ?? Environment.MachineName;
        string repository = Repository();
        string title = "EplanMcp.Spikes.S8" + revision;
        _title = title;
        _action = "EplanMcpSpikeS8" + revision;
        _addIn = Path.Combine(repository, "spikes", "S8-write-interruption", "bin", "Release", "net481", title + ".dll");
        string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } h ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        _scratch = Path.Combine(home, "scratch", "spikes", "s8");
        _project = Path.Combine(_scratch, "S8_Sample");
        _output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s8")).FullName;
        if (!File.Exists(_addIn))
        {
            Console.WriteLine("missing add-in " + _addIn);
            return 1;
        }

        foreach (string old in Directory.EnumerateFiles(_output, "s8-*"))
        {
            File.Delete(old);
        }

        // gRPC honours the proxy variables (api-notes B4); cleared for this process only.
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        try
        {
            if (!Step("closeRunningEplan", CloseEplanGracefully) || !Step("copyProject", () => CopyFresh(_project)) || !StartAndOpen("start"))
            {
                return 1;
            }

            // U2: ended before the commit, after 5 of 20 pages.
            Step("u2.write", () => Fire("slowwrite", "u2", "/First:80 /Count:20 /DelayMs:500 /HoldMs:15000"));
            Step("u2.waitFive", () => WaitMarker("u2", "created 5", TimeSpan.FromSeconds(60)));
            Step("u2.kill", Kill);
            if (!StartAndOpen("u2.restart") || !Step("u2.count", () => Probe("count", "u2-count")))
            {
                return 1;
            }

            // U3: ended after the commit, while the action still holds the main thread.
            Step("u3.write", () => Fire("slowwrite", "u3", "/First:120 /Count:5 /DelayMs:100 /HoldMs:20000"));
            Step("u3.waitCommitted", () => WaitMarker("u3", "committed", TimeSpan.FromSeconds(60)));
            Step("u3.kill", Kill);
            if (!StartAndOpen("u3.restart") || !Step("u3.count", () => Probe("count", "u3-count")))
            {
                return 1;
            }

            // U3b: ended a while after a write that committed and finished, with the project never closed.
            Step("u3b.write", () => Probe("slowwrite", "u3b", "/First:140 /Count:3 /DelayMs:0 /HoldMs:0"));
            Step("u3b.idle", () =>
            {
                Thread.Sleep(5000);
                return "waited 5 s";
            });
            Step("u3b.kill", Kill);
            if (!StartAndOpen("u3b.restart") || !Step("u3b.count", () => Probe("count", "u3b-count")))
            {
                return 1;
            }

            // U4: the native access violation inside an open SafetyPoint with three uncommitted pages.
            Step("u4.violation", () => Fire("violation", "u4", "/First:160"));
            Step("u4.outcome", () => WaitReportOrExit("u4", TimeSpan.FromSeconds(90)));
            if (!EplanRunning() && !StartAndOpen("u4.restart"))
            {
                return 1;
            }

            Step("u4.count", () => Probe("count", "u4-count"));
            return 0;
        }
        finally
        {
            if (EplanRunning() && _client is not null)
            {
                Step("close", () => Probe("close", "close"));
                Step("unregister", () => Call($"EplApiModuleAction /unregister:\"{_title}\" /Quiet", TimeSpan.FromSeconds(30)));
            }

            File.WriteAllText(Path.Combine(_output, "s8-steps.json"), _steps.ToString(Formatting.Indented));
        }
    }

    private static bool StartAndOpen(string name) =>
        Step(name + ".startEplan", StartEplan)
        && Step(name + ".register", () => Call($"EplApiModuleAction /register:\"{_addIn}\" /Quiet", TimeSpan.FromSeconds(60)))
        && Step(name + ".openProject", () => Call($"ProjectOpen /Project:\"{_project}.elk\" /Quiet", TimeSpan.FromSeconds(120)))
        && Step(name + ".projects", () => Probe("projects", name + "-projects"));

    private static object CloseEplanGracefully()
    {
        Process[] running = Process.GetProcessesByName("EPLAN");
        foreach (Process process in running)
        {
            process.CloseMainWindow();
            if (!process.WaitForExit(120_000))
            {
                throw new InvalidOperationException("EPLAN did not close within 120 s; windows: " + Windows(process.Id));
            }
        }

        return running.Length + " EPLAN closed";
    }

    private static object StartEplan()
    {
        Process.Start(new ProcessStartInfo(Path.Combine(EplanBin, "EPLAN.exe"), $"/Variant:\"{Variant}\"") { UseShellExecute = false });
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(180))
        {
            Thread.Sleep(3000);
            try
            {
                var client = new EplanRemoteClient();
                client.Connect(_host, _port, TimeSpan.FromSeconds(5));
                client.SynchronousMode = true;
                if (client.Ping())
                {
                    _client = client;
                    return "remote server up after " + Math.Round(clock.Elapsed.TotalSeconds) + " s";
                }
            }
            catch (Exception)
            {
                // not listening yet
            }
        }

        throw new TimeoutException("EPLAN's remote server did not come up within 180 s; windows: " + string.Join(" | ", Process.GetProcessesByName("EPLAN").Select(p => Windows(p.Id))));
    }

    /// <summary>
    /// One remote call under a watchdog: a modal dialog in EPLAN holds it. While it waits, the two dialogs EPLAN shows
    /// when it opens a project it was killed with (r1) are answered, for scratch projects only; any other dialog stops
    /// the run.
    /// </summary>
    private static object Call(string command, TimeSpan limit)
    {
        EplanRemoteClient client = _client ?? throw new InvalidOperationException("not connected");
        Task<bool> call = Task.Run(() => client.ExecuteAction(command));
        var clock = Stopwatch.StartNew();
        while (!call.Wait(1000))
        {
            AnswerDialogs();
            if (clock.Elapsed > limit)
            {
                throw new TimeoutException("no answer within " + limit.TotalSeconds + " s; EPLAN windows: " + string.Join(" | ", Process.GetProcessesByName("EPLAN").Select(p => Windows(p.Id))));
            }
        }

        return call.Result;
    }

    /// <summary>
    /// "检查相容性" (project not closed properly; check its data?): "以后" for the spike's own copy, so that it opens
    /// exactly as it was left on disk; "否" for any other scratch project (the eval fixture EPLAN reopens on start), so
    /// that it is not opened at all. "打开项目 / 无法打开项目", the notice after a "否": "确定".
    /// </summary>
    private static void AnswerDialogs()
    {
        // UI Automation waits for EPLAN's UI thread, which a write holds (r2): look at most two seconds.
        Task.Run(AnswerDialogsNow).Wait(2000);
    }

    private static void AnswerDialogsNow()
    {
        foreach (Process eplan in Process.GetProcessesByName("EPLAN"))
        {
            AutomationElement? main = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, eplan.Id));
            AutomationElement? dialog = main?.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            if (dialog is null)
            {
                continue;
            }

            string title = dialog.Current.Name;
            string text = string.Join(" ", dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)).Cast<AutomationElement>().Select(e => e.Current.Name));
            bool spikeCopy = text.IndexOf(@"\scratch\spikes\s8\", StringComparison.OrdinalIgnoreCase) >= 0;
            bool scratch = text.IndexOf(@"\EplanMcp\scratch\", StringComparison.OrdinalIgnoreCase) >= 0;
            string? answer = title switch
            {
                "检查相容性" when spikeCopy => "以后",
                "检查相容性" when scratch => "否",
                "打开项目" when scratch => "确定",
                _ => null,
            };
            if (answer is null)
            {
                return; // not ours to answer; the watchdog reports it
            }

            var button = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, answer)));
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            _dialogs.Add(new JObject { ["at"] = DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture), ["title"] = title, ["text"] = text, ["answer"] = answer });
            File.WriteAllText(Path.Combine(_output, "s8-dialogs.json"), _dialogs.ToString(Formatting.Indented));
        }
    }

    private static object Fire(string mode, string label, string extra) =>
        Call($"{_action} /Mode:{mode} /Report:\"{Report(label)}\" /ScratchRoot:\"{_scratch}\" {extra}", TimeSpan.FromSeconds(30));

    private static object Probe(string mode, string label, string extra = "")
    {
        Fire(mode, label, extra);
        string report = Report(label);
        var clock = Stopwatch.StartNew();
        while (!File.Exists(report))
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(120))
            {
                throw new TimeoutException("no report " + Path.GetFileName(report) + "; EPLAN windows: " + string.Join(" | ", Process.GetProcessesByName("EPLAN").Select(p => Windows(p.Id))));
            }

            AnswerDialogs();
            Thread.Sleep(200);
        }

        return JObject.Parse(File.ReadAllText(report));
    }

    private static object WaitMarker(string label, string line, TimeSpan limit)
    {
        string marker = Report(label) + ".marker";
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (File.Exists(marker) && File.ReadAllLines(marker).Contains(line))
            {
                return File.ReadAllText(marker).Trim().Replace("\n", ", ");
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException("marker '" + line + "' not reached; marker: " + (File.Exists(marker) ? File.ReadAllText(marker) : "missing"));
    }

    private static object WaitReportOrExit(string label, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (!EplanRunning())
            {
                _client = null;
                return "EPLAN exited; marker: " + (File.Exists(Report(label) + ".marker") ? File.ReadAllText(Report(label) + ".marker").Trim().Replace("\n", ", ") : "missing");
            }

            if (File.Exists(Report(label)))
            {
                return JObject.Parse(File.ReadAllText(Report(label)));
            }

            AnswerDialogs();
            Thread.Sleep(200);
        }

        throw new TimeoutException("neither a report nor an exit; EPLAN windows: " + string.Join(" | ", Process.GetProcessesByName("EPLAN").Select(p => Windows(p.Id))));
    }

    private static object Kill()
    {
        Process[] running = Process.GetProcessesByName("EPLAN");
        foreach (Process process in running)
        {
            process.Kill(); // TerminateProcess, as taskkill /F does
            process.WaitForExit(60_000);
        }

        _client = null;
        return running.Length + " EPLAN killed at " + DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool EplanRunning() => Process.GetProcessesByName("EPLAN").Length > 0;

    private static string Report(string label) => Path.Combine(_output, "s8-" + label + ".json");

    private static bool Step(string name, Func<object?> work)
    {
        var clock = Stopwatch.StartNew();
        var entry = new JObject { ["step"] = name, ["at"] = DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) };
        try
        {
            entry["result"] = JToken.FromObject(work() ?? "done");
            entry["ok"] = true;
        }
        catch (Exception ex)
        {
            entry["ok"] = false;
            entry["error"] = ex.GetType().Name + ": " + ex.Message;
        }

        entry["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
        _steps.Add(entry);
        File.WriteAllText(Path.Combine(_output, "s8-steps.json"), _steps.ToString(Formatting.Indented));
        string shown = entry["ok"]!.Value<bool>() ? Short(entry["result"]!) : "FAILED " + entry["error"];
        Console.WriteLine($"{name}: {shown} ({entry["seconds"]} s)");
        return entry["ok"]!.Value<bool>();
    }

    private static string Short(JToken token)
    {
        string text = token.Type == JTokenType.Object ? token.ToString(Formatting.None) : token.ToString();
        return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
    }

    /// <summary>The titles of a process's visible top-level windows, to name a dialog that blocks a call.</summary>
    private static string Windows(int processId)
    {
        var titles = new List<string>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out int owner);
            if (owner == processId && IsWindowVisible(window))
            {
                var text = new StringBuilder(512);
                GetWindowText(window, text, text.Capacity);
                titles.Add(text.Length > 0 ? text.ToString() : "(untitled)");
            }

            return true;
        }, IntPtr.Zero);
        return titles.Count == 0 ? "(none)" : string.Join(", ", titles);
    }

    /// <summary>A fresh copy of the sample project: its .elk file and .edb directory, as the eval fixture does (A12).</summary>
    private static object CopyFresh(string project)
    {
        if (Directory.Exists(project + ".edb"))
        {
            Directory.Delete(project + ".edb", recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.Copy(Template + ".elk", project + ".elk", overwrite: true);
        foreach (string file in Directory.EnumerateFiles(Template + ".edb", "*", SearchOption.AllDirectories))
        {
            string target = project + ".edb" + file.Substring((Template + ".edb").Length);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return project;
    }

    private static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EplanMcp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
