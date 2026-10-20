using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S12.Driver;

/// <summary>
/// Drives spike S12 in the running EPLAN through Remote Client (api-notes B4), like the S11 driver: registers the spike
/// add-in, copies the sample project to <c>&lt;scratch&gt;\spikes\s12\</c> and opens it, makes that copy the current
/// project (Ctrl+Z works on the current project, S10), runs the probe modes, then restores the previously current
/// project, closes the copies and unregisters the add-in. Ctrl+Z is <c>XEsUndoAction</c>, sent only while every open
/// project is a scratch copy and the spike's copy is current. Project copies go to <c>&lt;scratch&gt;\spikes\s12\copies\</c>,
/// the backup to <c>&lt;scratch&gt;\spikes\s12\backup\</c>; reports to <c>.local\spikes\s12\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S12.Driver.exe --host name [--revision rN] [--port 49152]</c>.</para>
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private const string Template = @"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project";
    private const string Macro = @"C:\Users\Public\EPLAN\Data\宏\Company name\EPLAN_Macro\203_Electrical_Engine\001_Option_1\Contactor_Feedback_Control.ema";
    private const string Name = "S12_Sample";

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
        string title = "EplanMcp.Spikes.S12" + revision;
        string action = "EplanMcpSpikeS12" + revision;
        string addIn = Path.Combine(repository, "spikes", "S12-destructive-backup", "bin", "Release", "net481", title + ".dll");
        string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } h ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        string scratch = Path.Combine(home, "scratch", "spikes", "s12");
        string project = Path.Combine(scratch, Name);
        string copies = Path.Combine(scratch, "copies");
        string backup = Path.Combine(scratch, "backup");
        string macro = Path.Combine(scratch, "macro.ema"); // non-ASCII paths do not reach the action intact (B4)
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s12")).FullName;
        if (!File.Exists(addIn))
        {
            Console.WriteLine("missing add-in " + addIn);
            return 1;
        }

        foreach (string old in Directory.EnumerateFiles(output, "s12-*.json*"))
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
            File.WriteAllText(Path.Combine(output, "s12-steps.json"), steps.ToString(Formatting.Indented));
            string shown = entry["ok"]!.Value<bool>() ? entry["result"]!.ToString(Formatting.None) : "FAILED " + entry["error"];
            Console.WriteLine($"{name}: {(shown.Length > 500 ? shown.Substring(0, 500) + "…" : shown)} ({entry["seconds"]} s)");
            return entry["ok"]!.Value<bool>();
        }

        // Schedules a mode and waits for the report it writes from the dispatcher.
        JObject Probe(string mode, string label, string extra = "", string name = Name)
        {
            string report = Path.Combine(output, "s12-" + label + ".json");
            client.ExecuteAction($"{action} /Mode:{mode} /Report:\"{report}\" /ScratchRoot:\"{scratch}\" /Name:{name} {extra}");
            var clock = Stopwatch.StartNew();
            while (!File.Exists(report))
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(960))
                {
                    throw new TimeoutException("no report " + Path.GetFileName(report));
                }

                Thread.Sleep(250);
            }

            JObject read = JObject.Parse(File.ReadAllText(report));
            openOutsideScratch = read["openOutsideScratch"]?.Value<int>() ?? -1;
            current = read["current"]?.Type == JTokenType.String ? read["current"]!.Value<string>() : null;
            return read;
        }

        object Undo()
        {
            if (openOutsideScratch != 0 || current != Name)
            {
                throw new InvalidOperationException("not sent: " + openOutsideScratch + " project(s) outside the scratch root are open, current project " + current);
            }

            return client.ExecuteAction("XEsUndoAction");
        }

        // Which files of the source project a step changed, and what the step left in a target directory.
        object Watched(string target, Func<object> step)
        {
            Dictionary<string, string> before = Snapshot(project + ".edb");
            Dictionary<string, string> elkBefore = Snapshot(scratch, SearchOption.TopDirectoryOnly);
            object result = step();
            Dictionary<string, string> after = Snapshot(project + ".edb");
            Dictionary<string, string> elkAfter = Snapshot(scratch, SearchOption.TopDirectoryOnly);
            return new JObject
            {
                ["result"] = JToken.FromObject(result),
                ["projectFilesChanged"] = new JArray(Diff(before, after).Take(40)),
                ["projectFilesChangedCount"] = Diff(before, after).Count(),
                ["scratchTopFilesChanged"] = new JArray(Diff(elkBefore, elkAfter)),
                ["sourceStillOnDisk"] = File.Exists(project + ".elk") && Directory.Exists(project + ".edb"),
                ["target"] = Size(target),
            };
        }

        if (!Step("connect", () =>
        {
            foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            client.Connect(host, port);
            client.SynchronousMode = true;
            return host + ":" + port;
        }))
        {
            return 1;
        }

        string? previous = null;
        try
        {
            Step("register", () => client.ExecuteAction($"EplApiModuleAction /register:\"{addIn}\" /Quiet"));
            foreach (string leftover in new[] { Name, "S12_Snapshot", "S12_All", "S12_AfterPrepare" })
            {
                Step("closeLeftover-" + leftover, () => Probe("close", "leftover-" + leftover, name: leftover));
            }

            if (!Step("copyProject", () =>
            {
                foreach (string directory in new[] { copies, backup })
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }

                Directory.CreateDirectory(copies);
                Directory.CreateDirectory(backup);
                CopyFresh(project);
                File.Copy(Macro, macro, overwrite: true);
                return Size(project + ".edb");
            }))
            {
                return 1;
            }

            Step("open", () => client.ExecuteAction($"ProjectOpen /Project:\"{project}.elk\" /Quiet"));
            Thread.Sleep(2000);
            Step("projects", () => Probe("projects", "projects"));
            previous = current;
            Step("select", () => Probe("select", "select"));
            Step("count", () => Probe("count", "count"));

            // 1. Copies of the open project, opened and counted.
            foreach ((string mode, string copy) in new[] { ("copy", "S12_Snapshot"), ("copyall", "S12_All") })
            {
                string destination = Path.Combine(copies, copy);
                Step(mode, () => Watched(destination + ".edb", () => Probe(mode, mode, $"/Out:\"{destination}\"")));
                Step("open-" + copy, () => client.ExecuteAction($"ProjectOpen /Project:\"{destination}.elk\" /Quiet"));
                Thread.Sleep(2000);
                Step("count-" + copy, () => Probe("count", "count-" + copy, name: copy));
                Step("close-" + copy, () => Probe("close", "close-" + copy, name: copy));
            }

            Step("select-again", () => Probe("select", "select-again"));

            // 2. Renumbering on probe pages with "?" tags from a macro (S10: NumberWithQuestionMark).
            Step("prepare", () => Probe("prepare", "prepare", $"/Macro:\"{macro}\""));

            // r2: a snapshot right after a committed write has that write (EPLAN writes each change to disk at once, A15).
            string afterPrepare = Path.Combine(copies, "S12_AfterPrepare");
            Step("copy-after-prepare", () => Probe("copy", "copy-after-prepare", $"/Out:\"{afterPrepare}\""));
            Step("open-S12_AfterPrepare", () => client.ExecuteAction($"ProjectOpen /Project:\"{afterPrepare}.elk\" /Quiet"));
            Thread.Sleep(2000);
            Step("count-S12_AfterPrepare", () => Probe("count", "count-S12_AfterPrepare", name: "S12_AfterPrepare"));
            Step("close-S12_AfterPrepare", () => Probe("close", "close-S12_AfterPrepare", name: "S12_AfterPrepare"));
            Step("select-after-copy", () => Probe("select", "select-after-copy"));
            string scheme = string.Empty;
            Step("renumberdevicesrollback", () =>
            {
                JObject read = Probe("renumberdevicesrollback", "renumberdevicesrollback");
                JToken? worked = read["attempts"]?.FirstOrDefault(a => a["result"]?["error"] is null);
                scheme = worked?["scheme"]?.Value<string>() ?? string.Empty;
                return new JObject { ["report"] = read, ["schemeUsed"] = scheme };
            });
            Step("renumberdevices", () => Probe("renumberdevices", "renumberdevices", $"/Scheme:\"{scheme}\""));
            Step("undo-renumberdevices", Undo);
            Step("count-after-undo-renumberdevices", () => Probe("count", "count-after-undo-renumberdevices"));
            Step("renumberpagesrollback", () => Probe("renumberpagesrollback", "renumberpagesrollback"));
            Step("renumberpages", () => Probe("renumberpages", "renumberpages"));
            Step("undo-renumberpages", Undo);
            Step("count-after-undo-renumberpages", () => Probe("count", "count-after-undo-renumberpages"));

            // 3. Page removal.
            Step("removerollback", () => Probe("removerollback", "removerollback"));
            Step("remove", () => Probe("remove", "remove"));
            Step("undo-remove", Undo);
            Step("count-after-undo-remove", () => Probe("count", "count-after-undo-remove"));

            // 4. Backup.Project(MakeBackup) last: its documentation says the project path becomes invalid afterwards.
            Step("backup", () => Watched(backup, () => Probe("backup", "backup", $"/Out:\"{backup}\"")));
            Step("projects-after-backup", () => Probe("projects", "projects-after-backup"));
        }
        finally
        {
            if (previous is not null && previous != Name)
            {
                Step("restore", () => Probe("restore", "restore", $"/Restore:\"{previous}\""));
            }

            Step("close", () => Probe("close", "close"));
            Step("unregister", () => client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet"));
        }

        return 0;
    }

    private static JObject Size(string path)
    {
        if (File.Exists(path))
        {
            return new JObject { ["files"] = 1, ["bytes"] = new FileInfo(path).Length };
        }

        if (!Directory.Exists(path))
        {
            string? parent = Path.GetDirectoryName(path);
            return new JObject
            {
                ["missing"] = path,
                ["parentFiles"] = parent is not null && Directory.Exists(parent)
                    ? new JArray(Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName).Take(20))
                    : new JArray(),
            };
        }

        FileInfo[] files = [.. Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f))];
        return new JObject { ["files"] = files.Length, ["bytes"] = files.Sum(f => f.Length) };
    }

    private static IEnumerable<string> Diff(Dictionary<string, string> before, Dictionary<string, string> after) =>
        after.Where(f => !before.TryGetValue(f.Key, out string? state) || state != f.Value).Select(f => f.Key).Concat(before.Keys.Where(k => !after.ContainsKey(k)));

    private static Dictionary<string, string> Snapshot(string directory, SearchOption option = SearchOption.AllDirectories) =>
        !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*", option).ToDictionary(
            f => f.Substring(directory.Length + 1),
            f => new FileInfo(f).Length.ToString(CultureInfo.InvariantCulture) + "@" + File.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>A fresh copy of the sample project: its .elk file and .edb directory, as the eval fixture does (A12).</summary>
    private static void CopyFresh(string project)
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
