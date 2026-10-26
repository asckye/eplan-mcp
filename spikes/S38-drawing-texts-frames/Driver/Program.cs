using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38.Driver;

internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private static readonly string[] _modes = ["texts", "replace", "titleblock", "findpage"];

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string file = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try
        {
            if (args.Contains("--help", StringComparer.Ordinal))
            {
                Console.WriteLine("--host <local IPv4> --port 49152 --revision rN --mode texts|replace|titleblock|findpage --project <scratch.elk> [--page <exact full name> (required except findpage)] [--free-id N --multi-id N --frame name --eplremote path]");
                return 0;
            }
            return Run(args);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    private static string? Option(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        if (i < 0) { return null; }
        if (i + 1 >= args.Length) { throw new ArgumentException("Missing value for " + key); }
        return args[i + 1];
    }

    private static string Quote(string text)
    {
        if (text.IndexOfAny(['"', '\r', '\n']) >= 0 || text.EndsWith("\\", StringComparison.Ordinal)) { throw new ArgumentException("Invalid command argument."); }
        return "\"" + text + "\"";
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Install the assembly resolver before loading Remote Client (B4).
    private static int Run(string[] args)
    {
        string project = Evidence.ProjectPath(Option(args, "--project") ?? throw new ArgumentException("--project is required."));
        string mode = Option(args, "--mode") ?? throw new ArgumentException("--mode is required.");
        string? page = Option(args, "--page");
        if (mode != "findpage" && string.IsNullOrEmpty(page)) { throw new ArgumentException("--page is required except for findpage."); }
        string revision = Option(args, "--revision") ?? throw new ArgumentException("--revision rN is required.");
        string host = Option(args, "--host") ?? throw new ArgumentException("--host must be this computer's IPv4 address (A41).");
        string port = Option(args, "--port") ?? "49152";
        if (!_modes.Contains(mode, StringComparer.Ordinal) || !Regex.IsMatch(revision, @"\Ar[1-9][0-9]*\z", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Use --revision rN and --mode texts|replace|titleblock|findpage.");
        }
        string root = Evidence.Repository();
        string assembly = "EplanMcp.Spikes.S38" + revision;
        string dll = Path.Combine(root, "spikes", "S38-drawing-texts-frames", "bin", "Release", "net481", assembly + ".dll");
        if (!File.Exists(dll)) { throw new FileNotFoundException("Build the requested add-in revision first.", dll); }
        string remote = Path.GetFullPath(Option(args, "--eplremote") ?? Path.Combine(root, ".local", "tools", "eplremote", "bin", "Release", "net481", "eplremote.exe"));
        if (mode == "replace" && !File.Exists(remote)) { throw new FileNotFoundException("replace requires the S35 eplremote client; pass --eplremote.", remote); }
        var request = new JObject { ["project"] = project, ["page"] = page, ["mode"] = mode };
        foreach (string key in new[] { "free", "multi" })
        {
            if (Option(args, "--" + key + "-id") is { } value) { request[key + "Id"] = long.Parse(value, CultureInfo.InvariantCulture); }
        }
        if (Option(args, "--frame") is { } frame) { request["frame"] = frame; }
        string output = Evidence.CreateRunDirectory();
        request["checkpoint"] = Path.Combine(output, mode + "-checkpoint.json");
        foreach (string proxy in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" }) { Environment.SetEnvironmentVariable(proxy, null); }
        using var client = new EplanRemoteClient();
        var session = new JObject { ["mode"] = mode, ["revision"] = revision };
        bool idle = true;
        bool registered = false;
        var watch = Stopwatch.StartNew();
        try
        {
            client.Connect(host, port);
            client.SynchronousMode = true;
            registered = client.ExecuteAction("EplApiModuleAction /register:" + Quote(dll) + " /Quiet");
            if (!registered) { throw new InvalidOperationException("Add-in registration returned false."); }
            JObject Invoke(string phase, JObject body)
            {
                string input = Path.Combine(output, phase + "-request.json");
                string report = Path.Combine(output, phase + ".json");
                Evidence.Write(input, body);
                idle = false;
                bool scheduled = client.ExecuteAction("EplanMcpSpikeS38" + revision + " /Request:" + Quote(input) + " /Report:" + Quote(report));
                var elapsed = Stopwatch.StartNew();
                while (!File.Exists(report) && scheduled && elapsed.Elapsed < TimeSpan.FromSeconds(180)) { Thread.Sleep(200); }
                if (!File.Exists(report)) { throw new TimeoutException("No completion report. Inspect EPLAN; no further action or unregister was attempted."); }
                idle = true;
                JObject result = JObject.Parse(File.ReadAllText(report));
                if (!scheduled || result["fatal"] is not null) { throw new InvalidOperationException("Probe refused or failed; inspect " + report); }
                return result;
            }
            JObject result = Invoke(mode, request);
            session["report"] = Path.Combine(output, mode + ".json");
            if (mode == "replace" && result["commit"]?.Value<bool?>("committed") == true)
            {
                var followup = (JObject)request.DeepClone();
                followup["baseline"] = result.DeepClone();
                followup["mode"] = "undo-check";
                JObject guard = Invoke("undo-check", followup);
                if (guard.Value<bool?>("undoAllowed") != true) { throw new InvalidOperationException("Undo preflight failed."); }
                var undo = new JObject { ["preflight"] = guard };
                result["undo"] = undo;
                Evidence.Write(Path.Combine(output, mode + ".json"), result);
                // A41/S35: one external eplremote invocation, after the committing action/locking scope has ended.
                var start = new ProcessStartInfo(remote, "--host " + Quote(host) + " XEsUndoAction")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                start.EnvironmentVariables["EPLREMOTE_PORT"] = port;
                var undoWatch = Stopwatch.StartNew();
                using (Process process = Process.Start(start)!)
                {
                    var stdout = new System.Text.StringBuilder();
                    var stderr = new System.Text.StringBuilder();
                    process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
                    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(30000))
                    {
                        idle = false;
                        undo["unknown"] = "eplremote did not exit in 30 s; no retry. Inspect the current scratch copy and dialogs.";
                        Evidence.Write(Path.Combine(output, mode + ".json"), result);
                        throw new TimeoutException(undo.Value<string>("unknown"));
                    }
                    process.WaitForExit(); // Drain asynchronous output events.
                    undo["exitCode"] = process.ExitCode;
                    undo["stdout"] = stdout.ToString();
                    undo["stderr"] = stderr.ToString();
                }
                undo["elapsedMs"] = undoWatch.Elapsed.TotalMilliseconds;
                Evidence.Write(Path.Combine(output, mode + ".json"), result);
                followup["mode"] = "undo-verify";
                undo["verification"] = Invoke("undo-verify", followup);
                Evidence.Write(Path.Combine(output, mode + ".json"), result);
            }
            bool complete = result["incomplete"] is null && result.Value<bool?>("hashEvidenceComplete") == true;
            if (mode == "texts")
            {
                Console.WriteLine("texts: " + result["inventory"]?["counts"]?.ToString(Newtonsoft.Json.Formatting.None));
                Console.WriteLine("by origin: " + result["inventory"]?["countsByOrigin"]?.ToString(Newtonsoft.Json.Formatting.None));
                Console.WriteLine("completeness: " + result["inventory"]?["completeness"]?.ToString(Newtonsoft.Json.Formatting.None));
                complete &= result["inventory"]?.Value<bool?>("complete") == true && result["designFileComparison"]?.Value<bool?>("equal") == true;
            }
            if (mode == "findpage")
            {
                JToken? scan = result["findpage"];
                Console.WriteLine("findpage: scanned=" + scan?["scannedPages"] + "; withFree=" + scan?["pagesWithFreeTexts"] + "; withMultiLanguage=" + scan?["pagesWithMultiLanguageTexts"] + "; replaceReady=" + scan?["replaceReadyPages"]);
                if (scan?["pages"] is JArray pages)
                {
                    foreach (JObject found in pages.OfType<JObject>().OrderByDescending(p => p.Value<bool>("replaceReady"))
                        .ThenByDescending(p => p.Value<int>("multiLanguageTextCount") > 0).Take(10))
                    {
                        Console.WriteLine(found.Value<string>("page") + "; single=" + found["singleLanguageTextCount"] + "; multi=" + found["multiLanguageTextCount"] + "; replaceReady=" + found["replaceReady"]);
                    }
                }
                complete &= scan?.Value<bool?>("complete") == true && result["designFileComparison"]?.Value<bool?>("equal") == true;
            }
            if (mode == "replace")
            {
                bool restored = result["undo"]?["verification"]?.Value<bool?>("oneUndoRestored") == true;
                Console.WriteLine("replace: rollback=" + result["replaceRollback"]?["restored"] + "; committed=" + result["commit"]?["committed"] + "; oneUndoRestored=" + restored);
                complete &= restored && result["undo"]?.Value<int?>("exitCode") == 0;
            }
            if (mode == "titleblock")
            {
                foreach (string probe in new[] { "pagePropertyProbe", "projectPropertyProbe", "frameProbe" })
                {
                    JToken? row = result[probe];
                    bool passed = row?.Value<bool?>("expectedReadback") == true && row.Value<bool?>("restored") == true && row.Value<bool?>("hashEvidenceComplete") == true;
                    Console.WriteLine(probe + ": " + (row?["skipped"]?.Value<string>() ?? "readback+rollback=" + passed));
                    complete &= passed;
                }
            }
            session["complete"] = complete;
            return complete ? 0 : 2; // A reachable API failure or missing sample is a result, not a successful proof.
        }
        catch (Exception ex) { session["error"] = Evidence.Error(ex); throw; }
        finally
        {
            session["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
            if (registered && idle)
            {
                try { session["unregisterReturned"] = client.ExecuteAction("EplApiModuleAction /unregister:" + Quote(assembly) + " /Quiet"); }
                catch (Exception ex) { session["unregisterError"] = Evidence.Error(ex); }
            }
            else if (registered) { session["unregisterSkipped"] = "Execution may still be active; retain the loaded add-in and inspect EPLAN."; }
            Evidence.Write(Path.Combine(output, "driver.json"), session);
            Console.WriteLine("reports: " + output);
        }
    }
}
