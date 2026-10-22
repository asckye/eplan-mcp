using System.Reflection;
using Eplan.EplApi.Starter;

namespace EplanMcp.Large.RevisionHelper;

/// <summary>
/// <c>EplanMcp.Large.RevisionHelper --variant-bin &lt;variant Bin&gt; --project &lt;copy&gt;.elk --name &lt;revision&gt;</c>:
/// opens the copy in EPLAN without a user interface, creates a revision in it and closes it again; prints the copy's link
/// file afterwards (<c>.ell</c> in S23 r3) on stdout. Exit codes: 0 done, 1 EPLAN failed, 2 wrong command line. Only for
/// copies the Large tests made in the scratch root: the test passes the path, and the revision cannot be undone (S23).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Dictionary<string, string> options = [];
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            options[args[i]] = args[i + 1];
        }

        if (!options.TryGetValue("--variant-bin", out string? variantBin) || !options.TryGetValue("--project", out string? project)
            || !options.TryGetValue("--name", out string? name) || !File.Exists(Path.Combine(variantBin, "W3u.exe")) || !File.Exists(project))
        {
            Console.Error.WriteLine("Usage: EplanMcp.Large.RevisionHelper --variant-bin <variant Bin, with W3u.exe> --project <existing copy>.elk --name <revision name>");
            return 2;
        }

        try
        {
            // As in the offline host: the variant's Bin for EPLAN's resolver, other assemblies from the Platform's (A22).
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(variantBin);
            resolver.PinToEplan();
            string platformBin = resolver.GetPlatformBinPath();
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string candidate = Path.Combine(platformBin, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            Console.Out.WriteLine(Revisions.Create(variantBin, project, name));
            return 0;
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e is not null; e = e.InnerException)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
            }

            return 1;
        }
    }
}
