using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S24.Driver;

/// <summary>
/// Drives the read part of spike S24 in the running EPLAN through Remote Client (api-notes B4): registers the spike
/// add-in, runs the parts and translation database probes, and unregisters it. No project is opened or changed.
/// Reports go to <c>.local\spikes\s24\</c> (git-ignored).
/// <para>Usage: <c>EplanMcp.Spikes.S24.Driver.exe --host name [--port 49152] [--part number] [--contains text]</c>.</para>
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
        string part = Option(args, "--part") ?? "SIE.3RH2122-1HB40";
        string contains = Option(args, "--contains") ?? "3RH21";
        string repository = Repository();
        const string Title = "EplanMcp.Spikes.S24";
        const string Action = "EplanMcpSpikeS24";
        string addIn = Path.Combine(repository, "spikes", "S24-master-data", "bin", "Release", "net481", Title + ".dll");
        string output = Directory.CreateDirectory(Path.Combine(repository, ".local", "spikes", "s24")).FullName;
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
            foreach (string mode in new[] { "partsdb", "translationdb" })
            {
                string report = Path.Combine(output, "s24-" + mode + ".json");
                File.Delete(report);
                client.ExecuteAction($"{Action} /Mode:{mode} /Report:\"{report}\" /Part:\"{part}\" /Contains:\"{contains}\"");
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
            Console.WriteLine("unregister: " + client.ExecuteAction($"EplApiModuleAction /unregister:\"{Title}\" /Quiet"));
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
