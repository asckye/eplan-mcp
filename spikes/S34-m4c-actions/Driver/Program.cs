using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S34.Driver;

/// <summary>
/// S34 Remote Client driver, following S32/S33 (api-notes B4). The engineer opens a scratch copy first.
/// Registration is undone in finally; the five built-in actions run inside the add-in using typed contexts.
/// </summary>
internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private static readonly string[] _modes = ["cables", "devicelist", "partslist", "plc", "pdfcomments"];

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // EPLAN types load only after the resolver is in place.
    private static int Run(string[] args)
    {
        string host = Option(args, "--host") ?? Environment.MachineName;
        string port = Option(args, "--port") ?? "49152";
        string project = Path.GetFullPath(Option(args, "--project") ?? throw new ArgumentException("--project is required."));
        string part = Option(args, "--part") ?? string.Empty;
        string revision = Option(args, "--revision") ?? string.Empty;
        string[] modes = (Option(args, "--modes") ?? string.Join(",", _modes)).Split(',').Select(m => m.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (modes.Any(m => !_modes.Contains(m, StringComparer.Ordinal)) || !Regex.IsMatch(revision, @"\A(?:r[1-9][0-9]*)?\z", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Use --revision rN and --modes cables,devicelist,partslist,plc,pdfcomments (or a subset).");
        }

        string scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + @"\";
        if (!project.StartsWith(scratch, StringComparison.OrdinalIgnoreCase) || !File.Exists(project))
        {
            throw new ArgumentException("--project must be an existing copy under %USERPROFILE%\\EplanMcp\\scratch\\.");
        }

        if (modes.Contains("pdfcomments", StringComparer.Ordinal))
        {
            part = Path.GetFullPath(part);
            if (!File.Exists(part) || !string.Equals(Path.GetExtension(part), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("--part must name the engineer-supplied PDF with comments for pdfcomments mode.");
            }
        }

        string repository = Repository();
        string title = "EplanMcp.Spikes.S34" + revision;
        string action = "EplanMcpSpikeS34" + revision;
        string addIn = Path.Combine(repository, "spikes", "S34-m4c-actions", "bin", "Release", "net481", title + ".dll");
        if (!File.Exists(addIn))
        {
            throw new FileNotFoundException("Build this add-in revision first.", addIn);
        }

        foreach (string value in new[] { project, part, addIn, repository })
        {
            if (value.Length > 1024 || value.IndexOfAny(['"', '\r', '\n']) >= 0)
            {
                throw new ArgumentException("Paths must be at most 1024 characters and contain no quotes or newlines.");
            }
        }

        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s34", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture))).FullName;
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        using var client = new EplanRemoteClient();
        client.Connect(host, port);
        client.SynchronousMode = true;
        bool failed = false;
        var session = new JObject { ["spike"] = "S34-driver", ["revision"] = revision, ["reports"] = new JArray() };
        try
        {
            // Follow S32/S33's Remote Client registration and scheduling calls here.
            // The generate/devicelist/partslist/plcservice/import probes never use this interface.
            session["registerReturned"] = client.ExecuteAction($"EplApiModuleAction /register:\"{addIn}\" /Quiet");
            if (session.Value<bool>("registerReturned") != true)
            {
                throw new InvalidOperationException("S34 add-in registration returned false.");
            }

            foreach (string mode in modes)
            {
                string report = Path.Combine(output, "s34-" + mode + ".json");
                Console.WriteLine("running " + mode + "; report: " + report);
                var entry = new JObject { ["mode"] = mode, ["file"] = Path.GetFileName(report) };
                ((JArray)session["reports"]!).Add(entry);
                try
                {
                    entry["scheduleReturned"] = client.ExecuteAction($"{action} /Mode:{mode} /Report:\"{report}\" /Project:\"{project}\" /Part:\"{part}\"");
                    if (entry.Value<bool>("scheduleReturned") != true)
                    {
                        throw new InvalidOperationException("S34 scheduling action returned false; inspect EPLAN and any refusal report.");
                    }

                    var clock = Stopwatch.StartNew();
                    while (!File.Exists(report) && clock.Elapsed < TimeSpan.FromSeconds(1860))
                    {
                        Thread.Sleep(250);
                    }

                    if (!File.Exists(report))
                    {
                        entry["error"] = "No report after 1860 s. Inspect EPLAN for dialogs or a running action; no subsequent mode was queued.";
                        failed = true;
                        break;
                    }

                    JObject result = JObject.Parse(File.ReadAllText(report));
                    int attemptCount = (result["attempts"] as JArray)?.Count ?? 0;
                    entry["attempts"] = attemptCount;
                    entry["fatal"] = result["fatal"]?.DeepClone();
                    string requiredType = mode switch { "cables" => "CABLES", "plc" => "BUSDATAIMPORT", "pdfcomments" => "PDFCOMMENTS", _ => "IMPORT" };
                    bool modeFailed = result["fatal"] is not null || result["attempts"] is not JArray attempts || !attempts.OfType<JObject>().Any(a => a.Value<bool?>("returned") == true && a["parameters"]?.Value<string>("TYPE") == requiredType && a["exception"] is null && a["scopeException"] is null);
                    if (result["attempts"] is JArray recorded)
                    {
                        entry["rollbackErrors"] = recorded.OfType<JObject>().Count(a => a["rollbackException"] is not null || a["scopeException"] is not null);
                        modeFailed |= entry.Value<int>("rollbackErrors") > 0;
                    }

                    entry["failed"] = modeFailed;
                    failed |= modeFailed;
                    Console.WriteLine(mode + ": " + attemptCount.ToString(CultureInfo.InvariantCulture) + " attempts; " + (modeFailed ? "inspect failures" : "report ready"));
                }
                catch (Exception ex)
                {
                    entry["error"] = ex.GetType().Name + ": " + ex.Message;
                    failed = true;
                    break; // Transport state is unknown; do not queue another potentially overlapping write.
                }
            }
        }
        finally
        {
            try
            {
                session["unregisterReturned"] = client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet");
                failed |= session.Value<bool>("unregisterReturned") != true;
            }
            catch (Exception ex)
            {
                session["unregisterError"] = ex.GetType().Name + ": " + ex.Message;
                failed = true;
            }

            string final = Path.Combine(output, "s34-driver.json");
            File.WriteAllText(final + ".partial", session.ToString(Formatting.Indented), new UTF8Encoding(false));
            File.Move(final + ".partial", final);
            Console.WriteLine("session: " + final);
        }

        return failed ? 1 : 0;
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
