using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S23.Driver;

/// <summary>
/// Drives spike S23 in the running EPLAN through Remote Client (api-notes B4), like the S21 driver: registers the spike
/// add-in, copies the sample project to <c>&lt;scratch&gt;\spikes\s23\</c> and opens it, makes that copy the current
/// project, runs the translation, language and revision probes, undoes the committed ones with Ctrl+Z, then restores the
/// previously current project, closes the copy and unregisters the add-in. Ctrl+Z is sent only while every open project
/// is a scratch copy and the copy is current. Reports go to <c>.local\spikes\s23\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S23.Driver.exe --host name [--revision rN] [--port 49152] [--follow-up]</c>.</para>
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private const string Template = @"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project";

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
        bool followUp = args.Contains("--follow-up"); // r1: display language forms and a translation with work to do
        string repository = Repository();
        string title = "EplanMcp.Spikes.S23" + revision;
        string action = "EplanMcpSpikeS23" + revision;
        string addIn = Path.Combine(repository, "spikes", "S23-translation-revisions", "bin", "Release", "net481", title + ".dll");
        string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } h ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        string scratch = Path.Combine(home, "scratch", "spikes", "s23");
        string project = Path.Combine(scratch, "S23_Sample");
        string exports = Path.Combine(scratch, "out");
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s23")).FullName;
        if (!File.Exists(addIn))
        {
            Console.WriteLine("missing add-in " + addIn);
            return 1;
        }

        foreach (string old in Directory.EnumerateFiles(output, "s23-*.json*"))
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
            File.WriteAllText(Path.Combine(output, "s23-steps.json"), steps.ToString(Formatting.Indented));
            string shown = entry["ok"]!.Value<bool>() ? entry["result"]!.ToString(Formatting.None) : "FAILED " + entry["error"];
            Console.WriteLine($"{name}: {(shown.Length > 400 ? shown.Substring(0, 400) + "…" : shown)} ({entry["seconds"]} s)");
            return entry["ok"]!.Value<bool>();
        }

        // Schedules a mode and waits for the report it writes from the dispatcher.
        JObject Probe(string mode, string label, string extra = "")
        {
            string report = Path.Combine(output, "s23-" + label + ".json");
            client.ExecuteAction($"{action} /Mode:{mode} /Report:\"{report}\" /ScratchRoot:\"{scratch}\" {extra}");
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
            if (openOutsideScratch != 0 || current != Path.GetFileName(project))
            {
                throw new InvalidOperationException("not sent: " + openOutsideScratch + " project(s) outside the scratch root are open, current project " + current);
            }

            return client.ExecuteAction("XEsUndoAction");
        }

        // Which project files a step changed, and which files appeared in the export folder.
        object Watched(string outDir, Func<object> step)
        {
            Dictionary<string, string> before = Snapshot(project + ".edb");
            Dictionary<string, string> scratchBefore = Snapshot(scratch);
            object result = step();
            Dictionary<string, string> after = Snapshot(project + ".edb");
            Dictionary<string, string> scratchAfter = Snapshot(scratch);
            return new JObject
            {
                ["result"] = JToken.FromObject(result),
                ["projectFilesChanged"] = new JArray(Diff(before, after)),
                ["newFilesOutsideOut"] = new JArray(scratchAfter.Keys.Except(scratchBefore.Keys).Where(k => !k.StartsWith("out", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("S23_Sample.edb", StringComparison.OrdinalIgnoreCase))),
                ["outFiles"] = Directory.Exists(outDir) ? new JArray(Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).Select(f => f.Substring(outDir.Length + 1) + " (" + new FileInfo(f).Length.ToString(CultureInfo.InvariantCulture) + " B)")) : new JArray(),
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
            Step("closeLeftover", () => Probe("close", "leftover"));
            if (!Step("copyProject", () =>
            {
                if (Directory.Exists(exports))
                {
                    Directory.Delete(exports, recursive: true);
                }

                return CopyFresh(project);
            }))
            {
                return 1;
            }

            Step("open", () => client.ExecuteAction($"ProjectOpen /Project:\"{project}.elk\" /Quiet"));
            Thread.Sleep(2000);
            Step("projects", () => Probe("projects", "projects"));
            previous = current;
            Step("select", () => Probe("select", "select"));
            Step("languages", () => Probe("languages", "languages"));
            if (followUp)
            {
                Step("displayformats", () => Watched(exports, () => Probe("displayformats", "displayformats")));
                Step("translateone", () => Watched(exports, () => Probe("translateone", "translateone")));
            }
            else
            {
                Step("texts", () => Probe("texts", "texts"));
                Step("translaterollback", () => Watched(exports, () => Probe("translaterollback", "translaterollback")));
                Step("translate", () => Watched(exports, () => Probe("translate", "translate")));
                Step("undo-translate", Undo);
                Step("texts-after-undo", () => Probe("texts", "texts-after-undo"));
                Step("displayrollback", () => Watched(exports, () => Probe("displayrollback", "displayrollback")));
                Step("removerollback", () => Watched(exports, () => Probe("removerollback", "removerollback")));
                Step("revisionstate", () => Probe("revisionstate", "revisionstate"));
                Step("createrevision", () => Watched(exports, () => Probe("createrevision", "createrevision")));
                Step("undo-revision", Undo);
                Step("revisionstate-after-undo", () => Probe("revisionstate", "revisionstate-after-undo"));
            }
        }
        finally
        {
            if (previous is not null && previous != Path.GetFileName(project))
            {
                Step("restore", () => Probe("restore", "restore", $"/Restore:\"{previous}\""));
            }

            Step("close", () => Probe("close", "close"));
            Step("unregister", () => client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet"));
        }

        return 0;
    }

    private static IEnumerable<string> Diff(Dictionary<string, string> before, Dictionary<string, string> after) =>
        after.Where(f => !before.TryGetValue(f.Key, out string? state) || state != f.Value).Select(f => f.Key).Concat(before.Keys.Where(k => !after.ContainsKey(k)));

    private static Dictionary<string, string> Snapshot(string directory) =>
        !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
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
}
