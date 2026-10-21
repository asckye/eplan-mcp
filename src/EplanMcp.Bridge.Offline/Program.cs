using System.Reflection;
using System.Text;
using Eplan.EplApi.Starter;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Hosting;
using EplanMcp.Bridge.Eplan.Hosting;

namespace EplanMcp.Bridge.Offline;

/// <summary>
/// Entry point of the offline host (M5 design 4.9); <see cref="OfflineHostOptions.Usage"/> gives the command line.
/// Points EPLAN's assembly resolver at EPLAN, then calls <see cref="OfflineEplan.Run"/>, the first method that touches an
/// EPLAN type, as in spike S15. Exit codes: 0 after a clean stop, 1 when EPLAN or the Bridge failed, 2 for a wrong
/// command line. Never writes to stdout; the log is <c>&lt;data root&gt;\logs\bridge-offline-yyyyMMdd.log</c>, and a
/// wrong command line or a failure also goes to stderr.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        OfflineHostOptions? options = OfflineHostOptions.Parse(args, out string? problem);
        if (options is not null)
        {
            problem = !File.Exists(Path.Combine(options.VariantBin, "W3u.exe"))
                ? $"--variant-bin must be the Bin folder of an EPLAN variant, which has W3u.exe; '{options.VariantBin}' has none (the Platform's Bin is not one)."
                : !File.Exists(options.Project) ? $"The project '{options.Project}' does not exist." : null;
        }

        if (problem is not null)
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine(OfflineHostOptions.Usage);
            return 2;
        }

        var log = new FileBridgeLog(DataLocations.Logs, "bridge-offline-");
        try
        {
            // The variant's Bin, from which EPLAN's resolver finds the Platform's. It resolves only EPLAN's own
            // assemblies; others in the Platform's Bin (Newtonsoft.Json, which the Bridge uses) are loaded here (A22).
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(options!.VariantBin);
            resolver.PinToEplan();
            string platformBin = resolver.GetPlatformBinPath();
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) => FromPlatformBin(platformBin, request.Name);
            return OfflineEplan.Run(options, platformBin, log);
        }
        catch (Exception ex)
        {
            // Types and messages only: before the resolver was in place, Exception.ToString() itself failed (S15).
            string failure = Describe(ex);
            log.Write(BridgeLogLevel.Error, "The offline host failed: " + failure);
            Console.Error.WriteLine("The offline host failed: " + failure);
            return 1;
        }
    }

    private static Assembly? FromPlatformBin(string platformBin, string name)
    {
        string candidate = Path.Combine(platformBin, new AssemblyName(name).Name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }

    private static string Describe(Exception exception)
    {
        var text = new StringBuilder();
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            text.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            text.Append(e is FileNotFoundException { FileName: { } file } ? " (" + file + ") " : " ");
        }

        return text.ToString().TrimEnd();
    }
}
