using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace EplanMcp.Workbench.Vm;

internal interface IVmElevation
{
    public Task<int?> RunAsync(string executable, bool remove, string port, CancellationToken cancellationToken);
}

internal sealed class VmElevation : IVmElevation
{
    internal static ProcessStartInfo StartInfo(string executable, bool remove, string port)
    {
        if (!Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetFileName(executable), "eplanmcp.exe", StringComparison.OrdinalIgnoreCase)
            || !VmNetwork.IsPort(port))
        {
            throw new ArgumentException("An absolute Host path and a port are required.");
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(executable)!, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "vm-firewall", remove ? "remove" : "add", "--port", int.Parse(port, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    public async Task<int?> RunAsync(string executable, bool remove, string port, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.Start(StartInfo(executable, remove, port)) ?? throw new InvalidOperationException("Host did not start.");
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null; // Windows reports a declined UAC prompt as ERROR_CANCELLED.
        }
    }
}
