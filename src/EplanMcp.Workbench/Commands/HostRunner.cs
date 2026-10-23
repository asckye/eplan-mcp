using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace EplanMcp.Workbench.Commands;

/// <summary>Why a Host command gave no result (ERR-01): each has its own text for the engineer.</summary>
internal enum HostFailure
{
    /// <summary>No <c>eplanmcp.exe</c> in either layout (NAV-05).</summary>
    Missing,

    NotStarted,

    TimedOut,

    /// <summary>An exit code other than 0 and 1, such as 2 for a command line this Host does not know.</summary>
    Failed,

    /// <summary>The output is not the documented JSON: the Workbench and the Host may be of different versions (ERR-08).</summary>
    Unreadable,
}

/// <summary>A Host command that gave no result, with the details an error shows below its text (ERR-02).</summary>
internal sealed class HostCommandException(HostFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public HostFailure Failure { get; } = failure;

    public string? CommandLine { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>The end of what the Host wrote to standard error, made plain; empty when it wrote nothing.</summary>
    public string Errors { get; init; } = string.Empty;
}

/// <summary>What one run of <c>eplanmcp.exe</c> gave: its exit code, its standard output and what it wrote to standard error.</summary>
internal sealed record HostRun(int ExitCode, string Output, string Errors = "");

/// <summary>Runs <c>eplanmcp.exe</c>; the one way the Workbench calls the Host (M5 design 4.4). Tests give a fake.</summary>
internal interface IHostRunner
{
    /// <summary>The program it runs, for the details of an error (ERR-02).</summary>
    public string Executable { get; }

    /// <summary>
    /// Runs the Host with <paramref name="arguments"/> and waits at most <paramref name="timeout"/> for it to exit.
    /// Throws <see cref="HostCommandException"/> when it cannot start or takes longer, and
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled first.
    /// </summary>
    public Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);

    public Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, string? input, CancellationToken cancellationToken) =>
        input is null ? RunAsync(arguments, timeout, cancellationToken) : throw new HostCommandException(HostFailure.NotStarted, "Stdin is not supported by this runner.");
}

/// <summary>
/// The real runner: the Host as a process of its own, started from an argument list, never through a shell, with no
/// window and its input closed after any supplied VM key. For add-in commands a timeout stops only the Host, leaving EPLAN for the engineer. The Host reads the data
/// root from the environment it inherits, so it is the Workbench's (ADR-0008).
/// </summary>
/// <param name="executable">The Host <see cref="ProgramLocation"/> found.</param>
/// <param name="environment">For tests: variables to set for the Host, such as its own data root.</param>
internal sealed class HostProcess(string executable, IReadOnlyDictionary<string, string>? environment = null) : IHostRunner
{
    public string Executable { get; } = executable;

    public Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) => RunAsync(arguments, timeout, null, cancellationToken);

    public async Task<HostRun> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, string? input, CancellationToken cancellationToken)
    {
        bool secret = input is not null || arguments.Contains("--vm");
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(Executable) ?? string.Empty,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            throw !secret ? new HostCommandException(HostFailure.NotStarted, ex.Message, ex) : new HostCommandException(HostFailure.NotStarted, "Could not start the VM command.");
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None); // read so that it never blocks, kept for the details
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            if (input is not null)
            { await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token).ConfigureAwait(false); }
            process.StandardInput.Close();
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            string text = await output.ConfigureAwait(false), stderr = await errors.ConfigureAwait(false);
            return new HostRun(process.ExitCode, secret ? HostCommands.VmOutput(text) : text, secret ? string.Empty : stderr);
        }
        catch (OperationCanceledException)
        {
            Stop(process, arguments[0] is not ("install-addin" or "uninstall-addin"));
            cancellationToken.ThrowIfCancellationRequested();
            throw new HostCommandException(HostFailure.TimedOut, string.Create(CultureInfo.InvariantCulture, $"eplanmcp did not finish within {timeout.TotalSeconds:0} s."))
            {
                Errors = secret ? string.Empty : await StoppedAsync(errors).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (secret && ex is not OutOfMemoryException)
        {
            Stop(process, children: true);
            throw new HostCommandException(HostFailure.Failed, "VM command input/output failed.");
        }
    }

    /// <summary>What a stopped process had written to standard error, if its end arrives soon.</summary>
    private static async Task<string> StoppedAsync(Task<string> errors)
    {
        try
        {
            return await errors.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return string.Empty;
        }
    }

    private static void Stop(Process process, bool children)
    {
        try
        {
            process.Kill(entireProcessTree: children);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // it ended on its own meanwhile
        }
    }
}
