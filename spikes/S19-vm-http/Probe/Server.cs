using System.Diagnostics;
using System.Security.Cryptography;

namespace EplanMcp.Spikes.S19.Probe;

/// <summary>
/// One run of a prototype exe as a child process with its own environment. In http mode it is ready when it prints
/// "S19 listening &lt;url&gt;" on stderr; the URL is always on 127.0.0.1 (the prototype refuses nothing else, and this
/// class checks it again). Stderr is kept for the README; stdout carries MCP only in stdio mode.
/// </summary>
internal sealed class Server : IAsyncDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource<Uri> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _firstStdout = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _stderr = [];

    private Server(Process process) => _process = process;

    public Uri Endpoint { get; private set; } = null!;

    public Process Process => _process;

    public IReadOnlyList<string> Stderr
    {
        get
        {
            lock (_stderr)
            {
                return [.. _stderr];
            }
        }
    }

    /// <summary>A fresh 256-bit key, base64url without padding (43 characters), as the Workbench would generate it.</summary>
    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Server Start(string exe, IEnumerable<string> arguments, IDictionary<string, string?> environment)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string name in start.Environment.Keys.Where(k => k.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("S19_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(name);
        }

        foreach ((string name, string? value) in environment)
        {
            start.Environment[name] = value;
        }

        var process = new Process { StartInfo = start };
        var server = new Server(process);
        process.ErrorDataReceived += (_, e) => server.OnStderr(e.Data);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                server._firstStdout.TrySetResult(line);
            }
        };
        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return server;
    }

    public async Task<Uri> WaitReadyAsync(TimeSpan timeout)
    {
        Endpoint = await _ready.Task.WaitAsync(timeout);
        if (!Endpoint.IsLoopback || Endpoint.Host != "127.0.0.1")
        {
            throw new InvalidOperationException("The prototype listens on " + Endpoint + ", not on 127.0.0.1. Stopping.");
        }

        return Endpoint;
    }

    public async Task<string> WriteAndReadLineAsync(string line, TimeSpan timeout)
    {
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
        return await _firstStdout.Task.WaitAsync(timeout);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync();
        _process.Dispose();
    }

    private void OnStderr(string? line)
    {
        if (line is null)
        {
            _ready.TrySetException(new InvalidOperationException("The prototype exited before it listened:\n" + string.Join('\n', Stderr)));
            return;
        }

        lock (_stderr)
        {
            _stderr.Add(line);
        }

        const string Marker = "S19 listening ";
        if (line.StartsWith(Marker, StringComparison.Ordinal))
        {
            string url = line[Marker.Length..].Split(' ')[0];
            _ready.TrySetResult(new Uri(url));
        }
    }
}
