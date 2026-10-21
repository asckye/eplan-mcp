using System.Diagnostics;

namespace EplanMcp.Host.Addin;

/// <summary>The real EPLAN processes. Never used by tests.</summary>
internal sealed class EplanProcesses : IEplanProcesses
{
    /// <summary>EPLAN.exe processes in this session; another user's EPLAN on a terminal server is not in the way.</summary>
    public IReadOnlyList<int> Running()
    {
        using Process current = Process.GetCurrentProcess();
        Process[] processes = Process.GetProcessesByName("EPLAN");
        try
        {
            return [.. processes.Where(p => p.SessionId == current.SessionId).Select(p => p.Id)];
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

    private sealed class Started(Process process) : IStartedEplan
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public void Refresh() => process.Refresh();

        /// <summary>Refreshes first as well, so that no caller sends the request to a window that is gone (see <see cref="IStartedEplan.Refresh"/>).</summary>
        public bool CloseMainWindow()
        {
            try
            {
                process.Refresh();
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
