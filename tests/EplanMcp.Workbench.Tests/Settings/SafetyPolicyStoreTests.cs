using EplanMcp.Workbench.Settings;

namespace EplanMcp.Workbench.Tests.Settings;

/// <summary>ADR-0061: the Workbench shows and changes the Host's write policy in config.json, and never loosens it by accident.</summary>
[Trait("Size", "Medium")]
public sealed class SafetyPolicyStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("eplanmcp-safety-policy-");

    private string Config => Path.Combine(_root.FullName, "config.json");

    public void Dispose() => _root.Delete(recursive: true);

    private SafetyPolicyStore Loaded()
    {
        var store = new SafetyPolicyStore(Config);
        store.Load();
        return store;
    }

    [Fact]
    public void WithoutAFile_ApprovalIsRequired_AsTheHostDecides()
    {
        SafetyPolicyStore store = Loaded();

        Assert.True(store.RequireNormalWriteApproval);
        Assert.False(store.WritesDenied);
        Assert.Equal(120, store.ApprovalTimeoutSeconds);
    }

    [Fact]
    public void TheSwitch_WritesAllowAndApprove_AndKeepsTheRestOfTheFile()
    {
        File.WriteAllText(Config, """{"scratchRoots":["C:\\Scratch"],"writesOutsideScratch":"approve"}""");
        SafetyPolicyStore store = Loaded();

        Assert.True(store.SetRequireNormalWriteApproval(false));
        Assert.False(store.RequireNormalWriteApproval);
        Assert.Contains("\"allow\"", File.ReadAllText(Config), StringComparison.Ordinal);

        Assert.True(store.SetRequireNormalWriteApproval(true));
        string text = File.ReadAllText(Config);
        Assert.Contains("\"approve\"", text, StringComparison.Ordinal);
        Assert.Contains("C:\\\\Scratch", text, StringComparison.Ordinal);
        Assert.True(Loaded().RequireNormalWriteApproval);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Deny_IsShownAsDenied_AndTheSwitchNeverLoosensIt(bool require)
    {
        const string denied = """{"writesOutsideScratch":"deny"}""";
        File.WriteAllText(Config, denied);
        SafetyPolicyStore store = Loaded();
        int notified = 0;
        store.Changed += () => notified++;

        Assert.True(store.WritesDenied);
        Assert.True(store.RequireNormalWriteApproval);
        Assert.False(store.SetRequireNormalWriteApproval(require));

        Assert.Equal(denied, File.ReadAllText(Config));
        Assert.True(store.WritesDenied);
        Assert.Equal(0, notified);
    }

    [Fact]
    public void AFileThatIsNotAnObject_IsLeftAlone_AndNothingChanges()
    {
        File.WriteAllText(Config, "[1, 2, 3]");
        SafetyPolicyStore store = Loaded();

        Assert.True(store.RequireNormalWriteApproval);
        Assert.False(store.SetRequireNormalWriteApproval(false));

        Assert.Equal("[1, 2, 3]", File.ReadAllText(Config));
        Assert.True(store.RequireNormalWriteApproval);
    }

    [Fact]
    public void AllowInAnyCase_IsRead_AndATimeoutOutsideTheRangeIsRefused()
    {
        File.WriteAllText(Config, """{"writesOutsideScratch":"Allow","approvalTimeoutSeconds":300}""");
        SafetyPolicyStore store = Loaded();

        Assert.False(store.RequireNormalWriteApproval);
        Assert.Equal(300, store.ApprovalTimeoutSeconds);
        Assert.False(store.SetApprovalTimeoutSeconds(30));
        Assert.True(store.SetApprovalTimeoutSeconds(60));
        Assert.Equal(60, Loaded().ApprovalTimeoutSeconds);
    }
}
