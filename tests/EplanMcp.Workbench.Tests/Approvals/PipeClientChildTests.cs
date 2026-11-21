using System.Diagnostics;
using EplanMcp.Workbench.Approvals;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// ADR-0065 decision 3 against real processes: a process this one started is its own child; this process's own parent
/// and an unrelated process are not.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PipeClientChildTests
{
    [Fact]
    public void AProcessThisOneStarted_IsItsOwnChild_AndOthersAreNot()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.True(PipeClient.IsOwnChild(child.Id));
            Assert.False(PipeClient.IsOwnChild(Environment.ProcessId));
            using Process explorer = Process.GetProcessesByName("explorer").FirstOrDefault() ?? Process.GetProcessById(4);
            Assert.False(PipeClient.IsOwnChild(explorer.Id));
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit();
        }
    }
}
