using EplanMcp.Build;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class TestImpactTests
{
    [Theory]
    [InlineData("Large.Tests/Acceptance.cs", true)]
    [InlineData("Host.Tests/ManualTime.cs", true)]
    [InlineData("Snapshots/help-json.verified.json", true)]
    [InlineData("Snapshots/server-contract.verified.json", false)]
    [InlineData("fixtures/input.json", true)]
    [InlineData("fixtures/nested/input.json", true)]
    [InlineData("fixtures/private/input.json", false)]
    [InlineData("assets/logo.svg", true)]
    [InlineData("assets/theme1.css", true)]
    [InlineData("assets/theme10.css", false)]
    [InlineData("Virtual/Internal.cs", false)]
    [InlineData("Unrelated/Unrelated.csproj", false)]
    [InlineData("Large.Tests/Unlinked.cs", false)]
    public void ExternalItems_MapPhysicalPaths(string changed, bool expected)
    {
        using Stream fixture = typeof(TestImpactTests).Assembly.GetManifestResourceStream("TestImpact.linked-project.csproj")!;
        using var reader = new StreamReader(fixture);
        string root = Path.GetFullPath("repository");
        Assert.Equal(expected, TestImpact.LinksFile(Path.Combine(root, "Tests", "Tests.csproj"), reader.ReadToEnd(), Path.Combine(root, changed)));
    }

    [Fact]
    public void NameStatus_IncludesBothRenamePathsAndDeletions()
    {
        string[] paths = TestImpact.ChangedPaths([
            "M\ttests/Host.Tests/ManualTime.cs",
            "R100\ttests/Large.Tests/Acceptance.cs\tdocs/new name.cs",
            "D\ttests/Snapshots/help-json.verified.json",
            "A\tdocs/new name.cs",
        ]);
        Assert.Equal([
            "tests/Host.Tests/ManualTime.cs", "tests/Large.Tests/Acceptance.cs",
            "docs/new name.cs", "tests/Snapshots/help-json.verified.json",
        ], paths);
    }
}
