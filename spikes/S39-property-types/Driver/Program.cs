using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Eplan.EplApi.RemoteClient;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S39.Driver;

internal static class Program
{
    private const string EplanBin = @"C:\Program Files\EPLAN\Platform\2026.0.3\Bin";
    private static readonly string[] _options = ["--host", "--port", "--project", "--revision"];
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(EplanBin, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] // Install the resolver before loading any EPLAN type (B4/S34).
    private static int Run(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !_options.Contains(args[i], StringComparer.Ordinal))
            { throw new ArgumentException("Use --host IPv4 --port 49152 --project sample.elk --revision rN."); }
            options.Add(args[i], args[i + 1]);
        }
        string Get(string key, string fallback) => options.TryGetValue(key, out string? value) ? value : fallback;
        string revision = Get("--revision", "r1");
        if (!Regex.IsMatch(revision, @"\Ar[1-9][0-9]*\z", RegexOptions.CultureInvariant)) { throw new ArgumentException("Revision must be rN."); }
        string dataRoot = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
        string fixture = Path.GetFullPath(Path.Combine(dataRoot, "scratch", "fixtures", "EPLAN_Sample_Project.elk"));
        string project = Path.GetFullPath(Get("--project", fixture));
        if (!string.Equals(project, fixture, StringComparison.OrdinalIgnoreCase) || !File.Exists(project))
        { throw new ArgumentException("--project must be the existing sample fixture under EPLANMCP_HOME (default %USERPROFILE%\\EplanMcp). Open it in EPLAN first."); }
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "EplanMcp.slnx"))) { repository = repository.Parent; }
        if (repository is null) { throw new DirectoryNotFoundException("Repository root not found."); }
        string title = "EplanMcp.Spikes.S39" + revision;
        string addIn = Path.Combine(repository.FullName, "spikes", "S39-property-types", "bin", "Release", "net481", title + ".dll");
        if (!File.Exists(addIn)) { throw new FileNotFoundException("Build this Release add-in revision first.", addIn); }
        string runId = Guid.NewGuid().ToString("N");
        string output = Directory.CreateDirectory(Path.Combine(repository.FullName, ".local", "spikes", "s39", runId)).FullName;
        string reportFile = Path.Combine(output, "s39-property-types.json");
        string Quote(string value)
        {
            if (value.Any(c => c == '"' || char.IsControl(c))) { throw new ArgumentException("Remote values must not contain quotes or control characters."); }
            return "\"" + value + "\"";
        }
        string register = "EplApiModuleAction /register:" + Quote(addIn) + " /Quiet";
        string action = "EplanMcpSpikeS39" + revision + " /Project:" + Quote(project) + " /Report:" + Quote(reportFile) + " /RunId:" + runId;
        foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "grpc_proxy", "ALL_PROXY", "all_proxy" })
        { Environment.SetEnvironmentVariable(variable, null); } // This driver process only (B4).
        using var client = new EplanRemoteClient();
        client.Connect(Get("--host", "127.0.0.1"), Get("--port", "49152"));
        client.SynchronousMode = true;
        JObject result;
        bool completed = false;
        bool unregistered;
        try
        {
            if (!client.ExecuteAction(register)) { throw new InvalidOperationException("Add-in registration returned false."); }
            if (!client.ExecuteAction(action)) { throw new InvalidOperationException("Scheduling returned false; inspect EPLAN system log."); }
            var watch = Stopwatch.StartNew();
            while (!File.Exists(reportFile) && watch.Elapsed < TimeSpan.FromSeconds(240)) { Thread.Sleep(250); }
            if (!File.Exists(reportFile)) { throw new TimeoutException("No report after 240 seconds; inspect EPLAN before retrying. Add-in: " + title); }
            result = JObject.Parse(File.ReadAllText(reportFile));
            completed = true;
            if ((string?)result["runId"] != runId || (string?)result["assembly"] != title) { throw new InvalidDataException("Report run/revision mismatch."); }
        }
        finally
        {
            // Unregister cannot unload an assembly (S2); a rebuild in the same session needs a new revision.
            unregistered = client.ExecuteAction("EplApiModuleAction /unregister:" + Quote(title) + " /Quiet");
            if (!unregistered) { Console.Error.WriteLine("Unregister returned false: " + title); }
            if (!completed) { Console.Error.WriteLine("Report: " + reportFile); }
        }
        var technical = result["samples"]?["20027"] as JArray ?? new JArray();
        var control = result["samples"]?["20011"] as JArray ?? new JArray();
        bool ok = unregistered && result["fatal"] is null && (result["readErrors"] as JArray)?.Count == 0 && technical.Count > 0 && control.Count > 0;
        string Types(JArray rows) => string.Join(",", rows.OfType<JObject>().Select(r => (string?)r["typeName"] + "(" + (string?)r["typeNumber"] + ")").Distinct(StringComparer.Ordinal));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "S39 {0}: 20027={1} [{2}]; 20011={3} [{4}]; report={5}", ok ? "OK" : "FAILED", technical.Count, Types(technical), control.Count, Types(control), reportFile));
        return ok ? 0 : 1;
    }
}
