using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using EplanMcp.Workbench.Settings;

namespace EplanMcp.Workbench.Vm;

internal sealed record VmEndpoint(string Address, int Port);
internal interface IVmChild : IDisposable
{
    public Task<string> Completion { get; }
    public Task<VmEndpoint> ListeningAsync(CancellationToken cancellationToken);
}
internal interface IVmLauncher
{
    public IVmChild Start(string executable, string address, string port, int? processId);
}

internal sealed class VmProcess : IVmLauncher
{
    internal static ProcessStartInfo StartInfo(string executable, string address, string port, int? processId)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.GetFileName(executable).Equals("eplanmcp.exe", StringComparison.OrdinalIgnoreCase)
            || !VmNetwork.IsAddress(address) || !VmNetwork.IsPort(port) || processId is <= 0)
        {
            throw new ArgumentException("An absolute Host path, canonical IPv4, port and positive PID are required.");
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (string argument in new[] { "serve", "--http", "--address", address, "--port", port }.Concat(processId is { } pid ? ["--process", pid.ToString(CultureInfo.InvariantCulture)] : Array.Empty<string>()))
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    public IVmChild Start(string executable, string address, string port, int? processId) => new Child(StartInfo(executable, address, port, processId));

    internal static VmEndpoint? ReadState(string text, int pid, DateTime started)
    {
        using JsonDocument json = JsonDocument.Parse(text);
        JsonElement value = json.RootElement;
        return value.GetProperty("processId").GetInt32() == pid && value.GetProperty("processStart").GetDateTimeOffset().UtcDateTime == started
            ? new(value.GetProperty("address").GetString()!, value.GetProperty("port").GetInt32()) : null;
    }

    private sealed class Child : IVmChild
    {
        private readonly Process _process;
        private readonly VmJob _job = new();
        private readonly int _pid;
        private readonly DateTime _started;
        private bool _disposed;
        public Child(ProcessStartInfo start)
        {
            _process = new Process { StartInfo = start };
            try
            {
                _process.Start();
                _job.Assign(_process.Handle); // From here on the Workbench's exit, even a crash, ends the service.
                (_pid, _started) = (_process.Id, _process.StartTime.ToUniversalTime()); // Monitoring outlives the process handle.
                Completion = CompleteAsync();
            }
            catch
            {
                try
                { _process.Kill(); } // A service the job does not hold must not keep listening.
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* Not started, or already gone. */ }
                _process.Dispose();
                _job.Dispose();
                throw;
            }
        }

        public Task<string> Completion { get; }
        public async Task<VmEndpoint> ListeningAsync(CancellationToken cancellationToken)
        {
            string state = Path.Combine(WorkbenchDataLocations.FromEnvironment().Root, "state");
            while (true)
            {
                foreach (string file in Directory.Exists(state) ? Directory.EnumerateFiles(state, FormattableString.Invariant($"http-{_pid}-*.json")) : [])
                {
                    try
                    {
                        if (ReadState(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), _pid, _started) is { } bound)
                        {
                            return bound;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException) { /* Stale or concurrently removed records are not listening signals. */ }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<string> CompleteAsync()
        {
            Task output = _process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var errors = new StringBuilder();
            char[] buffer = new char[2048];
            int count;
            while ((count = await _process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                errors.Append(buffer, 0, count);
                if (errors.Length > 16384)
                { errors.Remove(0, errors.Length - 16384); }
            }

            await _process.WaitForExitAsync().ConfigureAwait(false);
            await output.ConfigureAwait(false);
            return errors.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            { return; }
            _disposed = true;
            try
            { if (!_process.HasExited) { _process.Kill(); } } // Never kill EPLAN or a process tree.
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* The job remains the final stop guarantee. */ }
            _job.Dispose();
            _ = Completion.ContinueWith(_ => _process.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
