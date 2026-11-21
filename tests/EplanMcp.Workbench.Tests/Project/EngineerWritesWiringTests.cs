using System.Text.RegularExpressions;

namespace EplanMcp.Workbench.Tests.Project;

/// <summary>
/// ADR-0065 decision 3 in the running Workbench: the project page's tool client registers the engineer's writes with the
/// very object the approval server answers from. Built apart, the server never saw a click and asked every time.
/// </summary>
[Trait("Size", "Small")]
public sealed partial class EngineerWritesWiringTests
{
    [Fact]
    public void TheToolClientAndTheApprovalServer_ShareTheEngineersWrites()
    {
        string app = SourceScan.Single("App.xaml.cs").Text;

        Assert.Matches(ToolClient(), app);
        Assert.Matches(Server(), app);
    }

    [GeneratedRegex(@"new Project\.HostToolClient\(hostExecutable, _engineerWrites\)")]
    private static partial Regex ToolClient();

    [GeneratedRegex(@"new ApprovalServer\([^;]*_engineerWrites\)")]
    private static partial Regex Server();
}
