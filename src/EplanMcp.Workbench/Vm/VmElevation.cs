using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace EplanMcp.Workbench.Vm;

internal interface IVmElevation
{
    public Task<int?> RunAsync(string executable, bool remove, string port, string hostAddress, CancellationToken cancellationToken);
}

internal sealed class VmElevation : IVmElevation
{
    internal static ProcessStartInfo StartInfo(string executable, bool remove, string port, string hostAddress)
    {
        if (!Path.IsPathFullyQualified(executable) || !string.Equals(Path.GetFileName(executable), "eplanmcp.exe", StringComparison.OrdinalIgnoreCase)
            || !VmNetwork.IsPort(port) || !VmNetwork.IsAddress(hostAddress))
        {
            throw new ArgumentException("An absolute Host path, port and canonical IPv4 are required.");
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(executable)!, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "vm-firewall", remove ? "remove" : "add", "--port", int.Parse(port, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), "--host-address", hostAddress })
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    public async Task<int?> RunAsync(string executable, bool remove, string port, string hostAddress, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.Start(StartInfo(executable, remove, port, hostAddress)) ?? throw new InvalidOperationException("Host did not start.");
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null; // Windows reports a declined UAC prompt as ERROR_CANCELLED.
        }
    }
}
