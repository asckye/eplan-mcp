using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S37.Driver;

internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private static readonly string[] _options = ["--host", "--port", "--project", "--marker", "--mode", "--revision", "--pages", "--pages-file", "--copies", "--prefix", "--start", "--batch-size", "--numeration-mode", "--rollback", "--target-pages", "--generate-connections", "--allow-collisions", "--timeout-seconds"];

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" })) { return Checks.Run(); }
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string path = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            return Run(args);
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // No EPLAN types load before the resolver, or during --self-test.
    private static int Run(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)) { throw new ArgumentException("Expected --option value pairs."); }
            values.Add(args[i], args[i + 1]);
        }

        string Get(string key, string fallback = "") => values.TryGetValue("--" + key, out string? v) ? v : fallback;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mode"] = Get("mode", "count"), ["Project"] = ScratchPaths.Require(Get("project"), ".elk"),
            ["Marker"] = ScratchPaths.Require(Get("marker"), ".jsonl"), ["RunId"] = Guid.NewGuid().ToString("N"),
        };
        foreach (string key in values.Keys)
        {
            if (!_options.Contains(key, StringComparer.Ordinal))
            { throw new ArgumentException("Unknown option " + key); }
        }

        foreach (string pair in new[] { "Pages:pages", "Copies:copies", "Prefix:prefix", "Start:start", "BatchSize:batch-size", "NumerationMode:numeration-mode", "Rollback:rollback", "TargetPages:target-pages", "GenerateConnections:generate-connections", "AllowCollisions:allow-collisions" })
        {
            string[] parts = pair.Split(':');
            if (Get(parts[1]).Length != 0) { parameters.Add(parts[0], Get(parts[1])); }
        }

        if (Get("pages-file").Length != 0)
        {
            if (parameters.ContainsKey("Pages")) { throw new ArgumentException("Use either --pages or --pages-file."); }
            string[] pages = File.ReadAllLines(Get("pages-file")).Where(l => l.Length != 0).ToArray();
            if (pages.Length == 0) { throw new ArgumentException("The source-page file must not be empty."); }
            parameters["Pages"] = string.Join(";", pages);
        }

        string revision = Get("revision");
        if (revision.Any(c => !char.IsLetterOrDigit(c))) { throw new ArgumentException("Revision must be alphanumeric."); }
        string title = "EplanMcp.Spikes.S37" + revision;
        string dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "bin", "Release", "net481", title + ".dll"));
        if (!File.Exists(dll)) { throw new FileNotFoundException("Build the Release add-in first.", dll); }
        int timeout = int.Parse(Get("timeout-seconds", "7200"), CultureInfo.InvariantCulture);
        if (timeout < 1) { throw new ArgumentException("Timeout must be positive."); }
        Directory.CreateDirectory(Path.GetDirectoryName(parameters["Marker"])!);
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        { Environment.SetEnvironmentVariable(variable, null); } // Child process only, as S32.

        using var client = new EplanRemoteClient();
        Task<bool>? pending = null;
        bool Call(string command)
        {
            Console.WriteLine(command);
            pending = Task.Run(() => client.ExecuteAction(command));
            if (!pending.Wait(TimeSpan.FromSeconds(30))) { throw new TimeoutException("Remote action did not return. Outcome unknown; do not retry."); }
            return pending.GetAwaiter().GetResult(); // Synchronous console entry; no synchronization context.
        }

        client.Connect(Get("host", "127.0.0.1"), Get("port", "49152"));
        client.SynchronousMode = true;
        try
        {
            if (!Call("EplApiModuleAction /register:" + Quote(dll) + " /Quiet")) { throw new InvalidOperationException("Add-in registration failed."); }
            string action = "EplanMcpSpikeS37" + revision + " " + string.Join(" ", parameters.Select(p => "/" + p.Key + ":" + Quote(p.Value)));
            if (!Call(action)) { throw new InvalidOperationException("Action was refused (busy or invalid marker); inspect the marker and EPLAN system log."); }
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(timeout))
            {
                if (File.Exists(parameters["Marker"]))
                {
                    using var stream = new FileStream(parameters["Marker"], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    string? line;
                    JObject? completed = null;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        JObject row;
                        try { row = JObject.Parse(line); }
                        catch (JsonReaderException) { continue; } // The action may still be flushing the last line.
                        if ((string?)row["runId"] == parameters["RunId"] && (string?)row["event"] == "completed") { completed = row; }
                    }

                    if (completed is not null) { Console.WriteLine(completed.ToString(Formatting.None)); return (bool)completed["ok"]! ? 0 : 1; }
                }

                Thread.Sleep(500);
            }

            File.WriteAllText(ScratchPaths.Require(parameters["Marker"] + ".cancel", ".cancel"), "Driver timeout; inspect incomplete batches.");
            throw new TimeoutException("No completed marker. Cancellation requested between pages/batches; an active native call cannot be interrupted. Do not kill or retry EPLAN.");
        }
        finally
        {
            // Never send concurrent Remote Client commands after an unknown response.
            if (pending is null || pending.IsCompleted)
            {
                try { Console.WriteLine("unregister: " + Call("EplApiModuleAction /unregister:" + Quote(title) + " /Quiet")); }
                finally { if (pending is null || pending.IsCompleted) { client.Disconnect(); } }
            }
            else { Console.Error.WriteLine("Remote call still pending: unregister manually once EPLAN is responsive: EplApiModuleAction /unregister:" + title + " /Quiet"); }
        }
    }

    private static string Quote(string value)
    {
        if (value.Any(c => c == '"' || char.IsControl(c))) { throw new ArgumentException("Remote values must not contain quotes or control characters."); }
        return "\"" + value + "\"";
    }
}
