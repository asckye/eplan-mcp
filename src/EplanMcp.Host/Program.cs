namespace EplanMcp.Host;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            Console.WriteLine(HostInfo.Version);
            return 0;
        }

        Console.Error.WriteLine("usage: eplanmcp --version");
        return 2;
    }
}
