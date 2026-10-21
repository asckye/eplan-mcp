using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace EplanMcp.Workbench.Shell;

/// <summary>Opens a folder in Explorer, or a file with its default program: the pages' only way to hand a path to Windows.</summary>
internal interface IShellOpen
{
    /// <summary>False when Windows could not open it; the page then says so.</summary>
    public bool Open(string path);
}

/// <summary>Windows' shell: a path, never a command line, so nothing in it is run as one.</summary>
internal sealed class ShellOpen : IShellOpen
{
    public bool Open(string path)
    {
        if (!Path.Exists(path))
        {
            return false;
        }

        try
        {
            using Process? started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
