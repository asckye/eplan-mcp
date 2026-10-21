using System.Diagnostics;
using System.Globalization;
using System.Security;
using Microsoft.Win32;

namespace EplanMcp.Host.Addin;

/// <summary>The real EPLAN processes. Never used by tests.</summary>
internal sealed class EplanProcesses : IEplanProcesses
{
    /// <summary>
    /// EPLAN.exe processes in this session. The variant comes from where EPLAN registers each running instance,
    /// <c>HKCU\Software\EPLAN\RemoteServer\&lt;port&gt;</c> (api-notes A1, as the Bridge reads it); an instance not
    /// listed there has an unknown variant.
    /// </summary>
    public IReadOnlyList<RunningEplan> Running()
    {
        using Process current = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName("EPLAN");
        try
        {
            Dictionary<string, string> variants = RemoteServerVariants();
            return [.. processes.Where(p => p.SessionId == current.SessionId)
                .Select(p => new RunningEplan(p.Id, variants.GetValueOrDefault(p.Id.ToString(CultureInfo.InvariantCulture))))];
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Through the shell, so EPLAN inherits none of this process's handles: a caller reading eplanmcp's output (the
    /// installer, the Workbench) would otherwise wait until EPLAN exits. The working directory is EPLAN's Bin, so the
    /// EPLAN left open never holds the app folder an uninstaller deletes.
    /// </summary>
    public IStartedEplan Start(string exe, string arguments)
    {
        var start = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        return new Started(Process.Start(start) ?? throw new InvalidOperationException($"{exe} did not start."));
    }

    private static Dictionary<string, string> RemoteServerVariants()
    {
        var variants = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using RegistryKey? servers = Registry.CurrentUser.OpenSubKey(@"Software\EPLAN\RemoteServer");
            foreach (string port in servers?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? server = servers!.OpenSubKey(port);
                if (Convert.ToString(server?.GetValue("ProcessID"), CultureInfo.InvariantCulture) is { Length: > 0 } pid
                    && server!.GetValue("EplanProductVariant") is string { Length: > 0 } variant)
                {
                    variants[pid] = variant;
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable: every running EPLAN counts as one of unknown variant, which refuses rather than guesses.
        }

        return variants;
    }

    private sealed class Started(Process process) : IStartedEplan
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public bool CloseMainWindow()
        {
            try
            {
                return process.CloseMainWindow();
            }
            catch (InvalidOperationException)
            {
                return true; // it exited meanwhile, which is what closing asks for
            }
        }

        public void Dispose() => process.Dispose();
    }
}
