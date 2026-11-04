using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EplanMcp.Tests;

/// <summary>
/// WIN-15: a crashed test process reports on stderr and exits instead of waiting on the Windows error dialog, which
/// would hold an unattended gate or live run (seen live 2026-11-03 with EplanMcp.Bridge.Core.Tests.exe).
/// </summary>
internal static class NoCrashDialog
{
    internal const string CrashVariable = "EPLANMCP_TEST_CRASH";
    private const uint FailCriticalErrors = 0x0001;
    private const uint NoGpFaultErrorBox = 0x0002;

#pragma warning disable CA2255 // The error mode must be set before the first test, in every test assembly.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        _ = SetErrorMode(FailCriticalErrors | NoGpFaultErrorBox); // Returns the previous mode, not an error.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("Unhandled exception in test process: " + e.ExceptionObject);
        // NoCrashDialogTests starts this assembly with the variable set, to prove a real crash exits without a dialog.
        if (Environment.GetEnvironmentVariable(CrashVariable) == "1")
        {
            var crash = new Thread(() => throw new InvalidOperationException("Controlled crash"));
            crash.Start();
            crash.Join();
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
