using System.Diagnostics;
using EplanMcp.Workbench.Commands;

namespace EplanMcp.Workbench.Tests.Commands;

/// <summary>
/// The real runner with the real eplanmcp.exe built beside this repository's Workbench (STS-04): started without a
/// shell, its data root from the environment, its output read whole; and a program that takes too long is stopped.
/// </summary>
[Trait("Size", "Medium")]
public sealed class HostProcessTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-workbench-runner-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task TheBuiltHost_VerifiesTheAuditOfTheDataRootItIsGiven()
    {
        var host = new HostProcess(BuiltHost(), new Dictionary<string, string> { ["EPLANMCP_HOME"] = _root.FullName });

        HostRun run = await host.RunAsync(["audit", "verify", "--format", "json"], TimeSpan.FromSeconds(30), Token);

        using var report = System.Text.Json.JsonDocument.Parse(run.Output);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Path.Combine(_root.FullName, "audit"), report.RootElement.GetProperty("directory").GetString());
        Assert.False(report.RootElement.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task AnArgumentWithSpacesOrQuotes_ReachesTheHostAsOneArgument_AsItIs()
    {
        var host = new HostProcess(BuiltHost(), new Dictionary<string, string> { ["EPLANMCP_HOME"] = _root.FullName });
        TimeSpan limit = TimeSpan.FromSeconds(30);

        // "--format json" split in two, or "json" without its quotes, would be a valid command line: exit code 0.
        HostRun asOne = await host.RunAsync(["audit", "verify", "--format json"], limit, Token);
        HostRun quoted = await host.RunAsync(["audit", "verify", "--format", "\"json\""], limit, Token);
        HostRun valid = await host.RunAsync(["audit", "verify", "--format", "json"], limit, Token);

        Assert.Equal((2, 2, 0), (asOne.ExitCode, quoted.ExitCode, valid.ExitCode));
        Assert.StartsWith("usage: eplanmcp", asOne.Errors, StringComparison.Ordinal); // and standard error is kept
    }

    [Fact]
    public async Task AProgramThatTakesTooLong_IsStopped_AndTheRunTimesOut()
    {
        var ping = new HostProcess(Path.Combine(Environment.SystemDirectory, "PING.EXE"));
        var watch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<HostCommandException>(() => ping.RunAsync(["-n", "30", "127.0.0.1"], TimeSpan.FromSeconds(1), Token));

        Assert.Equal(HostFailure.TimedOut, error.Failure);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task CancellingTheRun_StopsTheProgram_AndCancels()
    {
        var ping = new HostProcess(Path.Combine(Environment.SystemDirectory, "PING.EXE"));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ping.RunAsync(["-n", "30", "127.0.0.1"], TimeSpan.FromSeconds(60), cancel.Token));
    }

    [Fact]
    public async Task AProgramThatIsNotThere_DoesNotStart()
    {
        var host = new HostProcess(Path.Combine(_root.FullName, "eplanmcp.exe"));

        var error = await Assert.ThrowsAsync<HostCommandException>(() => host.RunAsync(["doctor"], TimeSpan.FromSeconds(5), Token));

        Assert.Equal(HostFailure.NotStarted, error.Failure);
    }

    /// <summary>The Host the solution built with these tests, found as the Workbench in the repository finds it.</summary>
    private static string BuiltHost()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string repository = output.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;
        return HostLocator.Find(Path.Combine(repository, "src", "EplanMcp.Workbench", "bin", output.Parent.Name, "net10.0-windows"))
            ?? throw new InvalidOperationException("eplanmcp.exe is not built; build the solution first.");
    }
}
