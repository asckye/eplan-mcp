using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S42.Driver;

internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            Dictionary<string, string> options = Parse(args);
            if (options.ContainsKey("--validate-only")) { Console.WriteLine("S42 parameters valid; no EPLAN connection made."); return 0; }
            return Run(options);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal) { ["--fail"] = "false" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!seen.Add(key)) { throw new ArgumentException("Duplicate option: " + key); }
            if (key == "--validate-only") { options[key] = "true"; continue; }
            if (key is not ("--step" or "--fail" or "--log") || ++i == args.Length) { throw new ArgumentException("Use --step sentinel|create|move|view|readback --fail true|false --log .local/spikes/s42/<new>.jsonl [--validate-only]."); }
            if (args[i].IndexOfAny(['"', '\r', '\n']) >= 0) { throw new ArgumentException("Quotes and newlines are refused in parameters."); }
            options[key] = args[i];
        }
        if (!options.TryGetValue("--step", out string? step) || step is not ("sentinel" or "create" or "move" or "view" or "readback") ||
            options["--fail"] is not ("true" or "false") ||
            (options["--fail"] == "true" && step is not ("create" or "move"))) { throw new ArgumentException("Invalid step/fail; fail only applies to create or move."); }
        if (!options.TryGetValue("--log", out string? log) || string.IsNullOrWhiteSpace(log)) { throw new ArgumentException("Supply --log under .local/spikes/s42/."); }
        options["--log"] = Evidence.LogPath(log, false);
        return options;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] // B4/S36: install AssemblyResolve before touching Remote Client types.
    private static int Run(Dictionary<string, string> options)
    {
        string host = Environment.GetEnvironmentVariable("EPLREMOTE_HOST") ?? string.Empty;
        string port = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        if (!IPAddress.TryParse(host, out IPAddress? address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out ushort number) || number == 0) { throw new ArgumentException("Set EPLREMOTE_HOST to the local IPv4 and EPLREMOTE_PORT to its Remote Client port."); }
        string addIn = Path.Combine(Evidence.Repository(AppContext.BaseDirectory), "spikes", "S42-graphics-move-view", "bin", "Release", "net481", "EplanMcp.Spikes.S42.dll");
        if (!File.Exists(addIn)) { throw new FileNotFoundException("Build the S42 add-in in Release first."); }
        Evidence.ProjectPath(Evidence.Target);
        string log = Evidence.LogPath(options["--log"], true);
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" }) { Environment.SetEnvironmentVariable(variable, null); }
        using var client = new EplanRemoteClient();
        client.Connect(host, port, TimeSpan.FromSeconds(10));
        client.SynchronousMode = true;
        bool registrationAttempted = false, idle = true;
        try
        {
            registrationAttempted = true;
            idle = false;
            Call(client, $"EplApiModuleAction /register:\"{addIn}\" /Quiet", _ => idle = true);
            idle = false;
            Call(client, $"EplanMcpSpikeS42 /Step:{options["--step"]} /Fail:{options["--fail"]} /Project:\"{Evidence.Target}\" /Log:\"{log}\"", accepted => idle = !accepted);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(150))
            {
                if (File.Exists(log))
                {
                    using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string text = reader.ReadToEnd();
                    string? last = text.EndsWith("\n", StringComparison.Ordinal) ? text.TrimEnd('\n').Split('\n').LastOrDefault() : null;
                    if (last is not null && JObject.Parse(last) is JObject entry && (string?)entry["phase"] == "done")
                    {
                        idle = true;
                        Console.Write(text);
                        return (bool?)entry["data"]?["ok"] == true ? 0 : 1;
                    }
                }
                Thread.Sleep(250);
            }
            throw new TimeoutException("S42 outcome unknown; inspect EPLAN and the JSONL. Do not retry a write.");
        }
        finally
        {
            if (registrationAttempted && idle) { Call(client, "EplApiModuleAction /unregister:\"EplanMcp.Spikes.S42\" /Quiet"); }
            else if (registrationAttempted) { Console.Error.WriteLine("S42 may still be active; inspect it before manually unregistering EplanMcp.Spikes.S42."); }
        }
    }
    private static void Call(EplanRemoteClient client, string action, Action<bool>? returned = null)
    {
        Task<bool> call = Task.Run(() => client.ExecuteAction(action));
        // S36 synchronous process boundary; no EPLAN API runs on the driver's thread pool.
        if (!call.Wait(TimeSpan.FromSeconds(20))) { throw new TimeoutException("Remote action timed out; inspect EPLAN before continuing."); }
        bool accepted = call.Result;
        returned?.Invoke(accepted);
        if (!accepted) { throw new InvalidOperationException("Remote action returned false; inspect EPLAN and the log."); }
    }
}
