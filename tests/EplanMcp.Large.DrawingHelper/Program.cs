using System.Reflection;
using Eplan.EplApi.Starter;

namespace EplanMcp.Large.DrawingHelper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || !File.Exists(Path.Combine(args[0], "W3u.exe")) || !File.Exists(args[1]))
        {
            Console.Error.WriteLine("Usage: EplanMcp.Large.DrawingHelper <variant Bin> <scratch fixture copy.elk> <page>");
            return 2;
        }

        string scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(args[1]).StartsWith(scratch, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("The fixture copy must be inside the scratch root.");
            return 2;
        }

        try
        {
            var resolver = new AssemblyResolver();
            resolver.SetEplanBinPath(args[0]);
            resolver.PinToEplan();
            string platformBin = resolver.GetPlatformBinPath();
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string candidate = Path.Combine(platformBin, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
            Console.Out.WriteLine(Drawing.Read(args[0], args[1], args[2]));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
