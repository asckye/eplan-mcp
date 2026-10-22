using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S31.Driver;

/// <summary>
/// Drives spike S31 in the running EPLAN through Remote Client (api-notes B4): registers the spike add-in, runs the
/// given probes, and unregisters it. <c>extract</c> only reads; <c>reopen</c> and <c>marker</c> work only on
/// projects under the scratch root. Reports go to <c>.local\spikes\s31\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S31.Driver.exe --host name [--revision rN] [--port 49152] [--modes extract,reopen,marker]
/// [--project "x.elk"]</c>.</para>
/// </summary>
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
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // EPLAN types load only after the resolver is in place
    private static int Run(string[] args)
    {
        string port = Option(args, "--port") ?? "49152";
        string host = Option(args, "--host") ?? Environment.MachineName;
        string project = Option(args, "--project") ?? string.Empty;
        string part = Option(args, "--part") ?? string.Empty;
        string variant = Option(args, "--variant") ?? "-1";
        string revision = Option(args, "--revision") ?? string.Empty; // re-registering in the same EPLAN needs a new name
        string repository = Repository();
        string title = "EplanMcp.Spikes.S31" + revision;
        string action = "EplanMcpSpikeS31" + revision;
        string addIn = Path.Combine(repository, "spikes", "S31-snapshot", "bin", "Release", "net481", title + ".dll");
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s31")).FullName;
        if (!File.Exists(addIn))
        {
            Console.WriteLine("missing add-in " + addIn); // building another revision removes the previous one
            return 1;
        }

        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        {
            Environment.SetEnvironmentVariable(variable, null);
        }

        var client = new EplanRemoteClient();
        client.Connect(host, port);
        client.SynchronousMode = true;
        Console.WriteLine("register: " + client.ExecuteAction($"EplApiModuleAction /register:\"{addIn}\" /Quiet"));
        try
        {
            foreach (string mode in (Option(args, "--modes") ?? "extract").Split(','))
            {
                string report = Path.Combine(output, "s31-" + mode + ".json");
                File.Delete(report);
                client.ExecuteAction($"{action} /Mode:{mode} /Report:\"{report}\" /Project:\"{project}\" /Part:\"{part}\" /Variant:{variant}");
                var clock = Stopwatch.StartNew();
                while (!File.Exists(report) && clock.Elapsed < TimeSpan.FromSeconds(660))
                {
                    Thread.Sleep(250);
                }

                Console.WriteLine(mode + ": " + (File.Exists(report) ? JObject.Parse(File.ReadAllText(report)).ToString(Formatting.None) : "no report"));
            }
        }
        finally
        {
            Console.WriteLine("unregister: " + client.ExecuteAction($"EplApiModuleAction /unregister:\"{title}\" /Quiet"));
        }

        return 0;
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
