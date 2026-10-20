using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S9.Driver;

/// <summary>
/// Drives spike S9 in the running EPLAN through Remote Client (api-notes B4): registers the spike add-in, copies the
/// sample project to <c>&lt;scratch&gt;\spikes\s9\</c> and opens it, runs the probe modes in order, then always closes that
/// copy and unregisters the add-in. Writes only to that copy and to <c>.local\spikes\s9\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S9.Driver.exe [--revision rN] [--host name] [--port 49152]</c>. The host defaults to
/// this machine's name: EPLAN may listen on the network addresses only, not on the loopback (S9 first run).</para>
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private const string Template = @"C:\Users\Public\EPLAN\Data\项目\Company name\EPLAN_Sample_Project";
    // A schematic macro shipped with EPLAN; the Siemens 3RT2 one tried first has an ArticlePlacement representation only.
    private const string Macro = @"C:\Users\Public\EPLAN\Data\宏\Company name\EPLAN_Macro\203_Electrical_Engine\001_Option_1\Contactor_Feedback_Control.ema";

    private static readonly string[] _modes = ["symbols", "pages", "rename", "place", "placeauto", "macro", "connect"];

    private static int Main(string[] args)
    {
        // Remote Client, Remoting, gRPC and Newtonsoft come from the EPLAN installation, as in an add-in.
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
        string title = "EplanMcp.Spikes.S9" + revision;
        string action = "EplanMcpSpikeS9" + revision;
        string addIn = Path.Combine(repository, "spikes", "S9-write-api", "bin", "Release", "net481", title + ".dll");
        string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") is { Length: > 0 } h ? h : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        string scratch = Path.Combine(home, "scratch", "spikes", "s9");
        string project = Path.Combine(scratch, "S9_Sample");
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s9")).FullName;

        // Remote Client reports success even when the add-in is missing (api-notes A1), so check here, and never
        // read a report an earlier run left behind.
        if (!File.Exists(addIn))
        {
            Console.WriteLine("missing add-in " + addIn + "; build it with -p:AssemblyName=" + title + " -p:AssemblyTitle=" + title);
            return 1;
        }

        foreach (string old in Directory.EnumerateFiles(output, "s9-*.json"))
        {
            File.Delete(old);
        }

        var steps = new JArray();
        var client = new EplanRemoteClient();
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

        // A path with non-ASCII characters did not reach the action intact (first run: "EMA file does not exist"),
        // so the macro is copied to an ASCII path in the scratch folder first.
        string macro = Path.Combine(scratch, "macro.ema");
        string Probe(string mode) =>
            $"{action} /Mode:{mode} /Report:\"{Path.Combine(output, "s9-" + mode + ".json")}\" /ScratchRoot:\"{scratch}\" /Macro:\"{macro}\"";

        if (!Step("connect", () =>
        {
            // gRPC's C core honours HTTP_PROXY and friends; a local EPLAN must not be reached through a proxy (S9 first
            // run). Cleared for this process only, never globally.
            foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            client.Connect(host, port);
            client.SynchronousMode = true;
            return host + ":" + port;
        }))
        {
            File.WriteAllText(Path.Combine(output, "s9-steps.json"), steps.ToString(Formatting.Indented));
            return 1;
        }

        try
        {
            Step("register", () => client.ExecuteAction($"EplApiModuleAction /register:\"{addIn}\" /Quiet"));
            Step("closeLeftover", () => client.ExecuteAction(Probe("close")));
            if (!Step("copyProject", () =>
            {
                File.Copy(Macro, macro, overwrite: true);
                return CopyFresh(project);
            }))
            {
                return 1;
            }

            Step("open", () => client.ExecuteAction($"ProjectOpen /Project:\"{project}.elk\" /Quiet"));
            foreach (string mode in _modes)
            {
                Step(mode, () => client.ExecuteAction(Probe(mode)));
            }
        }
        finally
        {
            Step("close", () => client.ExecuteAction(Probe("close")));
            Step("unregister", () => client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet"));
            File.WriteAllText(Path.Combine(output, "s9-steps.json"), steps.ToString(Formatting.Indented));
        }

        return 0;
    }

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
