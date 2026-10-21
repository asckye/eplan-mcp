using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Eplan.EplApi.Starter;

namespace EplanMcp.Spikes.S15;

/// <summary>
/// Spike S15: <c>EplanMcp.Spikes.S15.exe &lt;variant Bin&gt; &lt;scratch project .elk&gt; &lt;result .json&gt;</c>. Points the
/// assembly resolver at EPLAN's Bin, then runs <see cref="Probes"/> in a method of its own, so no EPLAN type is loaded
/// before the resolver is in place. Writes nothing to the project it is given other than the pages Probes creates;
/// run it on a scratch copy only (CLAUDE.md rule 1).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: EplanMcp.Spikes.S15 <variant Bin> <scratch project .elk> <result .json>");
            return 2;
        }

        var clock = Stopwatch.StartNew();
        try
        {
            // args[0] is the variant's Bin (W3u.exe; EplApplication.EplanBinFolder): the Platform's Bin, where the
            // API assemblies are, is not a valid variant (first run). EPLAN's resolver finds the Platform from it but
            // loads only EPLAN's own assemblies; others there (Newtonsoft.Json) are loaded from it as well.
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(args[0]);
            resolver.PinToEplan();
            string platform = resolver.GetPlatformBinPath();
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string candidate = Path.Combine(platform, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            Console.Error.WriteLine("platform bin: " + platform);
            return Run(args[0], args[1], args[2], clock);
        }
        catch (Exception ex)
        {
            // Exception.ToString() itself failed once here, so only the parts that need no EPLAN type are printed.
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                Console.Error.WriteLine($"{clock.Elapsed.TotalSeconds:0.00}s {e.GetType().FullName}: {e.Message}");
                if (e is FileNotFoundException missing)
                {
                    Console.Error.WriteLine("  file: " + missing.FileName);
                }
            }

            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string bin, string project, string result, Stopwatch clock) =>
        new Probes(bin, project, result, clock).Run();
}
