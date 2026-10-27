using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S36.Driver;

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
            if (options.ContainsKey("--validate-only"))
            {
                Console.WriteLine("S36 parameters valid; no EPLAN connection made.");
                return 0;
            }

            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--variant"] = "default",
            ["--fail"] = "false",
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!seen.Add(key))
            {
                throw new ArgumentException("Duplicate option: " + key);
            }

            if (key == "--validate-only")
            {
                options[key] = "true";
                continue;
            }

            if (key is not ("--step" or "--variant" or "--fail" or "--log") || ++i == args.Length)
            {
                throw new ArgumentException("Use --step sentinel|apply|readback --variant default|false --fail true|false --log .local/...jsonl [--validate-only].");
            }

            options[key] = args[i];
        }

        if (!options.TryGetValue("--step", out string? step) || step is not ("sentinel" or "apply" or "readback") ||
            options["--variant"] is not ("default" or "false") || options["--fail"] is not ("true" or "false") ||
            (options["--fail"] == "true" && step != "apply"))
        {
            throw new ArgumentException("Invalid step/variant/fail; deliberate failure is allowed only for apply.");
        }

        if (!options.TryGetValue("--log", out string? log) || string.IsNullOrWhiteSpace(log) || log.IndexOfAny(['"', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Supply a fresh JSON-lines file with --log under this worktree's .local directory.");
        }

        string root = Repository();
        string fullLog = Path.GetFullPath(log);
        if (!fullLog.StartsWith(Path.Combine(root, ".local") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullLog), ".jsonl", StringComparison.OrdinalIgnoreCase) || File.Exists(fullLog))
        {
            throw new ArgumentException("Log must be a new file under this worktree's .local directory.");
        }

        options["--log"] = fullLog;
        return options;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Resolve EPLAN assemblies after installing the resolver (S32/B4).
    private static int Run(Dictionary<string, string> options)
    {
        string host = Environment.GetEnvironmentVariable("EPLREMOTE_HOST") ?? string.Empty;
        string port = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        if (!IPAddress.TryParse(host, out IPAddress? address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out ushort number) || number == 0)
        {
            throw new ArgumentException("Set EPLREMOTE_HOST to the dev machine's IPv4 and EPLREMOTE_PORT to its Remote Client port.");
        }

        string addIn = Path.Combine(Repository(), "spikes", "S36-module-commit", "bin", "Release", "net481", "EplanMcp.Spikes.S36.dll");
        if (!File.Exists(addIn))
        {
            throw new FileNotFoundException("Build the S36 add-in in Release first.");
        }

        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        string log = options["--log"];
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        string project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "writes", "LargeWrite.elk");
        using var client = new EplanRemoteClient();
        client.Connect(host, port, TimeSpan.FromSeconds(10));
        client.SynchronousMode = true;
        try
        {
            Call(client, $"EplApiModuleAction /register:\"{addIn}\" /Quiet");
            Call(client, $"EplanMcpSpikeS36 /Step:{options["--step"]} /Variant:{options["--variant"]} /Fail:{options["--fail"]} /Project:\"{project}\" /Log:\"{log}\"");
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(150))
            {
                if (File.Exists(log))
                {
                    using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string text = reader.ReadToEnd();
                    string? last = text.EndsWith("\n", StringComparison.Ordinal) ? text.TrimEnd('\n').Split('\n').LastOrDefault() : null;
                    if (last is not null)
                    {
                        JObject entry = JObject.Parse(last);
                        if ((string?)entry["phase"] == "done")
                        {
                            Console.Write(text);
                            return (bool?)entry["data"]?["ok"] == true ? 0 : 1;
                        }
                    }
                }

                Thread.Sleep(250);
            }

            throw new TimeoutException("S36 outcome unknown. Inspect EPLAN/dialogs and the JSONL; do not retry a write automatically.");
        }
        finally
        {
            Call(client, "EplApiModuleAction /unregister:\"EplanMcp.Spikes.S36\" /Quiet");
        }
    }

    private static void Call(EplanRemoteClient client, string action)
    {
        Task<bool> call = Task.Run(() => client.ExecuteAction(action));
        if (!call.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("Remote action timed out; inspect EPLAN before continuing. Registration cleanup may also need a manual retry.");
        }

        if (!call.Result)
        {
            throw new InvalidOperationException("Remote action returned false; stop and inspect EPLAN.");
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

        throw new InvalidOperationException("Run the driver from a build inside the worktree.");
    }
}
