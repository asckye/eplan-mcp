using System.Diagnostics;

namespace EplanMcp.Tests;

[Trait("Size", "Medium")]
public sealed class NoCrashDialogTests
{
    [Fact]
    public async Task ACrashedTestProcess_ExitsWithItsErrorOnStderr_WithoutWaitingOnADialog()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        start.Environment[NoCrashDialog.CrashVariable] = "1";
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            // A Windows error dialog would keep the process alive until this limit.
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Controlled crash", await error, StringComparison.Ordinal);
        _ = await output;
    }
}
