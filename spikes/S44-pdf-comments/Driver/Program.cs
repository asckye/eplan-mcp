using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S44.Driver;

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
                Console.WriteLine("S44 parameters valid; no EPLAN connection or run folder created.");
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
        var options = new Dictionary<string, string>(StringComparer.Ordinal) { ["--fail"] = "false" };
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

            if (key is not ("--step" or "--fail" or "--pdf") || ++i == args.Length)
            {
                throw new ArgumentException("Use --step sentinel|import|readback [--pdf <scratch/inputs/file.pdf>] [--fail true|false] [--validate-only].");
            }

            options[key] = args[i];
        }

        if (!options.TryGetValue("--step", out string? step) || step is not ("sentinel" or "import" or "readback") ||
            options["--fail"] is not ("true" or "false") || (options["--fail"] == "true" && step != "import"))
        {
            throw new ArgumentException("Invalid step/fail; deliberate failure is allowed only for import.");
        }

        if (step == "import")
        {
            if (!options.TryGetValue("--pdf", out string? pdf))
            {
                throw new ArgumentException("Import requires --pdf under scratch/inputs.");
            }

            options["--pdf"] = Paths.Input(pdf);
        }
        else if (options.ContainsKey("--pdf"))
        {
            throw new ArgumentException("Only import accepts --pdf.");
        }

        Paths.ProjectFiles(Paths.Project);
        Paths.RejectLinks(Paths.Runs);
        return options;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // Install the assembly resolver before resolving EPLAN types (S36/B4).
    private static int Run(Dictionary<string, string> options)
    {
        string host = Environment.GetEnvironmentVariable("EPLREMOTE_HOST") ?? string.Empty;
        string port = Environment.GetEnvironmentVariable("EPLREMOTE_PORT") ?? "49152";
        if (!IPAddress.TryParse(host, out IPAddress? address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !ushort.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out ushort number) || number == 0)
        {
            throw new ArgumentException("Set EPLREMOTE_HOST to the dev machine's IPv4 and EPLREMOTE_PORT to its Remote Client port.");
        }

        string addIn = Path.Combine(Repository(), "spikes", "S44-pdf-comments", "bin", "Release", "net481", "EplanMcp.Spikes.S44.dll");
        Paths.Checked(addIn);
        if (!File.Exists(addIn))
        {
            throw new FileNotFoundException("Build the S44 add-in in Release first.");
        }

        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        {
            Environment.SetEnvironmentVariable(variable, null); // Only this driver's environment is changed (S36).
        }

        string folder = Path.Combine(Paths.Runs, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(folder) || File.Exists(folder))
        {
            throw new IOException("Run folder already exists; refuse reuse.");
        }

        Paths.RejectLinks(folder);
        Directory.CreateDirectory(folder);
        string log = Path.Combine(folder, "events.jsonl");
        Paths.RunFile(log);
        string source = options.TryGetValue("--pdf", out string? input) ? input : string.Empty;
        string pdf = source.Length == 0 ? string.Empty : Path.Combine(folder, "input.pdf");
        if (source.Length != 0)
        {
            // Original PDF is opened read-only, without write/delete sharing; never parse or modify it.
            using var original = new FileStream(Paths.Input(source), FileMode.Open, FileAccess.Read, FileShare.Read);
            using (var copy = new FileStream(pdf, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                original.CopyTo(copy);
            }

            Paths.RunFile(pdf);
            if (!string.Equals(Paths.Hash(source), Paths.Hash(pdf), StringComparison.Ordinal))
            {
                throw new IOException("PDF copy hash mismatch; no EPLAN connection made.");
            }
        }

        File.WriteAllText(Path.Combine(folder, "driver.json"), JsonConvert.SerializeObject(new
        {
            step = options["--step"],
            fail = options["--fail"],
            project = Paths.Project,
            source,
            copy = pdf,
            sha256 = pdf.Length == 0 ? null : Paths.Hash(pdf),
            annotationList = "skipped",
            log,
        }), new UTF8Encoding(false));
        Console.Error.WriteLine("S44 run folder: " + folder);
        using var client = new EplanRemoteClient();
        client.Connect(host, port, TimeSpan.FromSeconds(10));
        client.SynchronousMode = true;
        try
        {
            Call(client, $"EplApiModuleAction /register:\"{addIn}\" /Quiet");
            Call(client, $"EplanMcpSpikeS44 /Step:{options["--step"]} /Fail:{options["--fail"]} /Project:\"{Paths.Project}\" /Source:\"{source}\" /Pdf:\"{pdf}\" /Log:\"{log}\"");
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

            throw new TimeoutException("S44 outcome unknown. Inspect EPLAN/dialogs and JSONL; do not retry a write automatically.");
        }
        finally
        {
            Call(client, "EplApiModuleAction /unregister:\"EplanMcp.Spikes.S44\" /Quiet");
        }
    }

    private static void Call(EplanRemoteClient client, string action)
    {
        Task<bool> call = Task.Run(() => client.ExecuteAction(action));
        if (!call.Wait(TimeSpan.FromSeconds(20)))
        {
            throw new TimeoutException("Remote action timed out; inspect EPLAN. Registration cleanup may require a manual retry.");
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
