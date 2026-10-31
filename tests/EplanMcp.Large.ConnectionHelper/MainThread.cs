using System.Diagnostics;
using System.Windows.Threading;
namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Action entry owns the test thread; shared TransactionScope uses the same assertion as the add-in.</summary>
internal static class MainThread
{
    private static Dispatcher? _main;
    public static void Use(Dispatcher main) => _main = main;
    public static void Assert() => Debug.Assert(_main?.CheckAccess() == true);
}
