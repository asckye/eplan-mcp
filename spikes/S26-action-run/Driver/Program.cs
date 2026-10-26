using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26.Driver;

internal static class Program
{
    private static readonly string _eplanBin = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "EplanBinPath").Value!;

    private static int Main(string[] args)
    {
        // Only the child worker touches Remote Client; help/validation/watchdog never load EPLAN.
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(_eplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            return Bootstrap(args);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Resolve Newtonsoft only after Main installs AssemblyResolve (S34/B4).
    private static int Bootstrap(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
#if S26_R3
            Console.WriteLine("S26r3 --project <open scratch .elk> --revision r3 [--modes r2-sync,r2-type,r2-macros,r2-filling,r2-exports] [--output <existing s26/UTC-id>] [--host host] [--port 49152]");
#elif S26_R2
            Console.WriteLine("S26r2 --project <open scratch .elk> --revision r2 [--modes r2-sync,r2-type,r2-macros,r2-filling,r2-exports] [--output <existing s26/UTC-id>] [--host host] [--port 49152]");
#else
            Console.WriteLine("S26 --project <open scratch .elk> [--modes runner,layers,parts,plc,properties,external] [--output <existing s26/UTC-id>] [--revision rN] [--host host] [--port 49152] [--common-scheme-node <verified USER node>] [--article-scheme-node <verified USER node>]");
#endif
            Console.WriteLine("Each mode has a watchdog including connect/register/schedule/unregister. No mode can run twice in one output folder. The driver never starts/closes EPLAN or opens/closes projects.");
#if S26_R2
            Console.WriteLine("Batch r2 modes: r2-sync,r2-type,r2-macros,r2-filling,r2-exports. Exports: --common-scheme-node <verified USER node> --common-scheme <existing XML scheme> --export-token <existing full object name> --address-token <existing PLC address>.");
#endif
            return 0;
        }
        if (args.Length == 2 && args[0] == "--worker") { return Worker(args[1]); }
        return Coordinate(Parse(args));
    }

    private static JObject Parse(string[] args)
    {
        string[] allowed = ["--project", "--modes", "--output", "--revision", "--host", "--port", "--common-scheme-node", "--article-scheme-node"];
#if S26_R2
        allowed = [.. allowed, "--common-scheme", "--export-token", "--address-token"];
#endif
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal) || options.ContainsKey(args[i])) { throw new ArgumentException("Unknown, duplicate or incomplete argument. See --help."); }
            options.Add(args[i], args[i + 1]);
        }
        string Get(string key, string fallback) => options.TryGetValue(key, out string? value) ? value : fallback;
        string project = SpikeS26Paths.Project(Get("--project", ""));
        string revision = Get("--revision", "");
        if (!Regex.IsMatch(revision, @"\A(?:r[1-9][0-9]*)?\z", RegexOptions.CultureInvariant)) { throw new ArgumentException("Use --revision rN matching the built assembly suffix."); }
#if S26_R2
#if S26_R3
        if (revision != "r3") { throw new ArgumentException("The r3 driver requires --revision r3."); }
#else
        if (revision != "r2") { throw new ArgumentException("The r2 driver requires --revision r2."); }
#endif
        string defaults = string.Join(",", SpikeS26Paths.Timeouts.Keys.Where(mode => mode.StartsWith("r2-", StringComparison.Ordinal)));
#else
        string defaults = string.Join(",", SpikeS26Paths.Timeouts.Keys);
#endif
        string[] modes = Get("--modes", defaults).Split(',').Select(SpikeS26Paths.Mode).ToArray();
        if (modes.Distinct(StringComparer.Ordinal).Count() != modes.Length) { throw new ArgumentException("A mode may occur only once."); }
        string folder = options.TryGetValue("--output", out string? output) ? SpikeS26Paths.RunFolder(output) : SpikeS26Paths.NewRunFolder();
        string title = "EplanMcp.Spikes.S26" + revision;
        string addIn = Path.Combine(Repository(), "spikes", "S26-action-run", "bin", "Release", "net481", title + ".dll");
#if S26_R3
        addIn = Path.Combine(Repository(), "spikes", "S26-action-run", "R3", "bin", "Release", "net481", title + ".dll");
#elif S26_R2
        addIn = Path.Combine(Repository(), "spikes", "S26-action-run", "R2", "bin", "Release", "net481", title + ".dll");
#endif
        if (!File.Exists(addIn)) { throw new FileNotFoundException("Build this add-in revision first.", addIn); }
        if (addIn.IndexOfAny(['"', '\r', '\n']) >= 0) { throw new ArgumentException("The registration path contains unsupported text-command characters."); }
        foreach (string mode in modes)
        {
            if (File.Exists(Path.Combine(folder, mode + ".started")) || File.Exists(SpikeS26Paths.Report(folder, mode)) ||
                File.Exists(Path.Combine(folder, mode + ".claimed")) || Directory.Exists(folder) && Directory.GetFileSystemEntries(folder, mode + "-*").Length > 0)
            {
                throw new IOException("Refusing to run mode twice in the same output folder: " + mode);
            }
        }
        string port = Get("--port", "49152");
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > 65535) { throw new ArgumentException("Invalid port."); }
        Directory.CreateDirectory(folder);
        SpikeS26Paths.RunFolder(folder); // Check again after creation.
        var request = new JObject
        {
            ["project"] = project, ["folder"] = folder, ["revision"] = revision, ["title"] = title,
            ["action"] = "EplanMcpSpikeS26" + revision, ["addIn"] = addIn,
            ["host"] = Get("--host", Environment.MachineName), ["port"] = port, ["modes"] = new JArray(modes),
            ["commonSchemeNode"] = Get("--common-scheme-node", ""), ["articleSchemeNode"] = Get("--article-scheme-node", ""),
        };
#if S26_R2
        request["r2"] = new JObject { ["commonScheme"] = Get("--common-scheme", ""), ["exportToken"] = Get("--export-token", ""), ["addressToken"] = Get("--address-token", "") };
#endif
        return request;
    }

    private static int Coordinate(JObject request)
    {
        string folder = request.Value<string>("folder")!;
        var session = new JObject { ["spike"] = "S26-driver", ["timestampUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["outputFolder"] = folder, ["modes"] = new JArray() };
        bool failed = false;
        string sessionPath = Path.Combine(folder, "s26-driver-" + Guid.NewGuid().ToString("N") + ".json");
        foreach (string mode in request["modes"]!.Values<string>().OfType<string>())
        {
            // Mark before starting a worker; never remove this marker, even on timeout/crash.
            SpikeS26Paths.Reserve(folder, mode, ".started");
            request["mode"] = mode;
            string workerReport = Path.Combine(folder, "s26-" + mode + "-driver.json");
            var entry = new JObject { ["mode"] = mode, ["timeoutSeconds"] = SpikeS26Paths.Timeouts[mode], ["report"] = SpikeS26Paths.Report(folder, mode), ["workerReport"] = workerReport };
            ((JArray)session["modes"]!).Add(entry);
            Atomic(sessionPath, session);
            Console.WriteLine("running " + mode + "; report: " + entry.Value<string>("report"));
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location)
            {
                Arguments = "--worker " + Encode(request), UseShellExecute = false, CreateNoWindow = true,
            };
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("Cannot start driver worker.");
            if (!worker.WaitForExit(SpikeS26Paths.Timeouts[mode] * 1000))
            {
                worker.Kill(); // This is our Remote Client worker, NEVER EPLAN.
                entry["timedOut"] = true;
                entry["error"] = "Worker exceeded the mode deadline. EPLAN's native action may still be running; no subsequent mode was queued. Registration cleanup may be incomplete; inspect EPLAN and unregister manually after it is idle.";
                failed = true;
                Atomic(sessionPath, session);
                break;
            }
            entry["exitCode"] = worker.ExitCode;
            failed |= worker.ExitCode != 0;
            if (File.Exists(workerReport)) { entry["worker"] = JObject.Parse(File.ReadAllText(workerReport)); }
            Atomic(sessionPath, session);
            if (worker.ExitCode == 2 || !File.Exists(workerReport)) { break; } // Transport/modal/rollback state is unknown.
        }
        Console.WriteLine("session: " + sessionPath);
        return failed ? 1 : 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Worker(string encoded)
    {
        JObject request = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
        string mode = SpikeS26Paths.Mode(request.Value<string>("mode")!);
        string folder = SpikeS26Paths.RunFolder(request.Value<string>("folder")!);
        string report = SpikeS26Paths.Report(folder, mode);
        string title = request.Value<string>("title")!;
        var session = new JObject { ["mode"] = mode };
        int code = 0;
        // Local process environment only, as in S34; never modify the user's environment.
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" }) { Environment.SetEnvironmentVariable(variable, null); }
        using var client = new EplanRemoteClient();
        try
        {
            client.Connect(request.Value<string>("host")!, request.Value<string>("port")!);
            client.SynchronousMode = true;
            session["registerReturned"] = client.ExecuteAction($"EplApiModuleAction /register:\"{request.Value<string>("addIn")}\" /Quiet");
            if (session.Value<bool?>("registerReturned") != true) { throw new InvalidOperationException("S26 registration returned false."); }
            // Only registration/scheduling use text commands. All built-ins use typed contexts inside EPLAN.
            // Base64 keeps project/output Unicode and all test strings away from Remote Client's text parser (A30).
            session["scheduleReturned"] = client.ExecuteAction(request.Value<string>("action") + " /RequestBase64:" + Encode(request));
            if (session.Value<bool?>("scheduleReturned") != true) { throw new InvalidOperationException("S26 scheduling refused. Inspect claimed markers and EPLAN's window state."); }
            while (!File.Exists(report)) { Thread.Sleep(250); } // Parent watchdog bounds this AND every synchronous Remote Client call.
            JObject result = JObject.Parse(File.ReadAllText(report));
            session["fatal"] = result["fatal"]?.DeepClone();
            if (result["fatal"] is not null || result["stopReason"] is not null) { code = 2; }
            var attempts = result["attempts"] as JArray ?? [];
            session["candidates"] = attempts.Count;
            session["notRun"] = attempts.OfType<JObject>().Count(attempt => attempt["notRun"] is not null);
            session["failedActions"] = attempts.OfType<JObject>().Count(attempt => attempt["notRun"] is null &&
                (attempt.Value<bool?>("returned") != true || attempt["exception"] is not null || attempt["setupException"] is not null));
            if (session.Value<int>("failedActions") > 0 || session.Value<int>("notRun") > 0) { code = Math.Max(1, code); }
            if (attempts.OfType<JObject>().Any(attempt => attempt.Value<bool?>("unsafeToContinue") == true)) { code = 2; }
#if S26_R2
            session["unverifiedCandidates"] = attempts.OfType<JObject>().Count(attempt => attempt["r2Assessment"] is JObject assessment && assessment.Value<string>("status") != "evidence_ready_for_review");
            if (session.Value<int>("unverifiedCandidates") > 0) { code = Math.Max(1, code); }
#endif
        }
        catch (Exception ex) { session["error"] = ex.GetType().Name + ": " + ex.Message; code = 2; }
        finally
        {
            session["cleanup"] = "pending";
            Atomic(Path.Combine(folder, "s26-" + mode + "-driver.json"), session);
            try
            {
                session["unregisterReturned"] = client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet");
                if (session.Value<bool?>("unregisterReturned") != true) { code = 2; }
            }
            catch (Exception ex) { session["unregisterError"] = ex.GetType().Name + ": " + ex.Message; code = 2; }
            session["cleanup"] = session.Value<bool?>("unregisterReturned") == true ? "unregistered" : "inspect";
            session["exitCode"] = code;
            Atomic(Path.Combine(folder, "s26-" + mode + "-driver.json"), session);
        }
        return code;
    }

    private static string Encode(JObject value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString(Formatting.None)));

    private static void Atomic(string path, JObject value)
    {
        string full = SpikeS26Paths.Below(path, SpikeS26Paths.OutputRoot);
        File.WriteAllText(full + ".partial", value.ToString(Formatting.Indented), new UTF8Encoding(false));
        if (File.Exists(full)) { File.Replace(full + ".partial", full, null); }
        else { File.Move(full + ".partial", full); }
    }

    private static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("EplanMcp.slnx not found above the driver.");
    }
}
