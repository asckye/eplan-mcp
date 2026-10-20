using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S10.Driver;

/// <summary>
/// Drives spike S10 in the running EPLAN through Remote Client (api-notes B4), like the S9 driver: registers the spike
/// add-in, copies the sample project to <c>&lt;scratch&gt;\spikes\s10\</c> and opens it, runs the probe modes, then
/// always closes that copy and unregisters the add-in. The probes run in EPLAN's dispatcher after their action has
/// returned, so each step waits for its report. Ctrl+Z is <c>XEsUndoAction</c> (S2). Writes only to that copy and to
/// <c>.local\spikes\s10\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S10.Driver.exe --host name [--revision rN] [--port 49152]</c>. EPLAN listens on
/// the network addresses only (api-notes A1), so the host is this machine's network address.</para>
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private const string Template = @"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project";
    private const string Macro = @"C:\Users\Public\EPLAN\Data\宏\Company name\EPLAN_Macro\203_Electrical_Engine\001_Option_1\Contactor_Feedback_Control.ema";

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
        string port = Option(args, "--port") ?? "49152";
        string host = Option(args, "--host") ?? Environment.MachineName;
        string repository = Repository();
        string title = "EplanMcp.Spikes.S10" + revision;
        string action = "EplanMcpSpikeS10" + revision;
        string addIn = Path.Combine(repository, "spikes", "S10-dispatcher-writes", "bin", "Release", "net481", title + ".dll");
        string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } h ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        string scratch = Path.Combine(home, "scratch", "spikes", "s10");
        string project = Path.Combine(scratch, "S10_Sample");
        string macro = Path.Combine(scratch, "macro.ema"); // non-ASCII paths do not reach the action intact (B4)
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s10")).FullName;
        if (!File.Exists(addIn))
        {
            Console.WriteLine("missing add-in " + addIn + "; build it with -p:AssemblyName=" + title + " -p:AssemblyTitle=" + title);
            return 1;
        }

        foreach (string old in Directory.EnumerateFiles(output, "s10-*.json*"))
        {
            File.Delete(old);
        }

        var steps = new JArray();
        var client = new EplanRemoteClient();
        int openOutsideScratch = -1;
        string? current = null;
        bool Step(string name, Func<object?> work)
        {
            var clock = Stopwatch.StartNew();
            var entry = new JObject { ["step"] = name };
            try
            {
                entry["result"] = JToken.FromObject(work() ?? "done");
                entry["ok"] = true;
            }
            catch (Exception ex)
            {
                entry["ok"] = false;
                entry["error"] = ex.GetType().FullName + ": " + ex.Message;
            }

            entry["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 2);
            steps.Add(entry);
            Console.WriteLine($"{name}: {(entry["ok"]!.Value<bool>() ? "ok" : entry["error"])} ({entry["seconds"]} s)");
            return entry["ok"]!.Value<bool>();
        }

        // Schedules a mode and waits for the report it writes from the dispatcher.
        object Probe(string mode, string label, string? extra = null)
        {
            string report = Path.Combine(output, "s10-" + label + ".json");
            client.ExecuteAction($"{action} /Mode:{mode} /Report:\"{report}\" /ScratchRoot:\"{scratch}\" /Macro:\"{extra ?? macro}\"");
            var clock = Stopwatch.StartNew();
            while (!File.Exists(report))
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(150))
                {
                    throw new TimeoutException("no report " + Path.GetFileName(report));
                }

                Thread.Sleep(200);
            }

            JObject read = JObject.Parse(File.ReadAllText(report));
            openOutsideScratch = read["openOutsideScratch"]?.Value<int>() ?? -1;
            current = read["current"]?.Type == JTokenType.String ? read["current"]!.Value<string>() : null;
            return read["fatal"] is JToken fatal ? "fatal: " + fatal : "report " + Path.GetFileName(report);
        }

        // Ctrl+Z takes back EPLAN's last undo step in whichever project it is: only when the last report saw nothing but
        // scratch copies open (CLAUDE.md rule 1).
        object Undo()
        {
            if (openOutsideScratch != 0 || current != Path.GetFileName(project))
            {
                throw new InvalidOperationException("not sent: " + openOutsideScratch + " project(s) outside the scratch root are open, current project " + current);
            }

            return client.ExecuteAction("XEsUndoAction");
        }

        // Which design data files a step changed (A11: any action rewrites User.eod/.eox).
        object Changed(Func<object> step)
        {
            Dictionary<string, string> before = Snapshot(project + ".edb");
            object result = step();
            Dictionary<string, string> after = Snapshot(project + ".edb");
            return new JObject
            {
                ["result"] = JToken.FromObject(result),
                ["changed"] = new JArray(after.Where(f => !before.TryGetValue(f.Key, out string? state) || state != f.Value).Select(f => f.Key)
                    .Concat(before.Keys.Where(k => !after.ContainsKey(k)))),
            };
        }

        if (!Step("connect", () =>
        {
            // gRPC honours the proxy variables (B4); cleared for this process only.
            foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            client.Connect(host, port);
            client.SynchronousMode = true;
            return host + ":" + port;
        }))
        {
            File.WriteAllText(Path.Combine(output, "s10-steps.json"), steps.ToString(Formatting.Indented));
            return 1;
        }

        string? previous = null;
        try
        {
            Step("register", () => client.ExecuteAction($"EplApiModuleAction /register:\"{addIn}\" /Quiet"));
            Step("closeLeftover", () => client.ExecuteAction($"{action} /Mode:close /Report:\"{Path.Combine(output, "s10-leftover.json")}\" /ScratchRoot:\"{scratch}\""));
            Thread.Sleep(2000);
            if (!Step("copyProject", () =>
            {
                Directory.CreateDirectory(scratch);
                File.Copy(Macro, macro, overwrite: true);
                return CopyFresh(project);
            }))
            {
                return 1;
            }

            Step("open", () => client.ExecuteAction($"ProjectOpen /Project:\"{project}.elk\" /Quiet"));
            Thread.Sleep(2000); // let EPLAN settle after opening before the file snapshots
            Step("projects", () => Probe("projects", "projects"));
            previous = current;
            Step("select", () => Probe("select", "select"));
            Step("noop", () => Changed(() => Probe("noop", "noop")));
            Step("pages", () => Probe("pages", "pages"));
            Step("macroinfo", () => Changed(() => Probe("macroinfo", "macroinfo")));
            Step("numbering", () => Probe("numbering", "numbering"));
            foreach (string mode in new[] { "undomacro", "connect", "symbol" })
            {
                Step(mode, () => Probe(mode, mode));
                Step("undo-" + mode, Undo);
                Step("count-after-undo-" + mode, () => Probe("count", "count-after-undo-" + mode));
            }
        }
        finally
        {
            if (previous is not null && previous != Path.GetFileName(project))
            {
                Step("restore", () => Probe("restore", "restore", previous));
            }

            Step("close", () => Probe("close", "close"));
            Step("unregister", () => client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet"));
            File.WriteAllText(Path.Combine(output, "s10-steps.json"), steps.ToString(Formatting.Indented));
        }

        return 0;
    }

    private static Dictionary<string, string> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            f => f.Substring(directory.Length + 1),
            f => new FileInfo(f).Length.ToString(CultureInfo.InvariantCulture) + "@" + File.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>A fresh copy of the sample project: its .elk file and .edb directory, as the eval fixture does (A12).</summary>
    private static string CopyFresh(string project)
    {
        if (Directory.Exists(project + ".edb"))
        {
            Directory.Delete(project + ".edb", recursive: true);
        }

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
}
