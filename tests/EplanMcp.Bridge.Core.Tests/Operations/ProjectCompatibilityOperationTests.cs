using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// project.compatibility (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.7) against project files in a temporary
/// directory: EPLAN's answer as it gives it, whether the project is open, and paths refused before EPLAN is asked.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectCompatibilityOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-compatibility-").FullName;
    private readonly FakeSnapshotReader _files = new();
    private readonly ProjectCompatibilityOperation _operation;

    public ProjectCompatibilityOperationTests() =>
        _operation = new ProjectCompatibilityOperation(new FakeCatalog(new OpenProject { Name = "Open", Path = Path.Combine(_root, "Open") }), _files);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("IsCompatible", true)]
    [InlineData("NeedsUpgrade", false)]
    public void Compatibility_IsWhatEplanSays(string status, bool compatible)
    {
        _files.Status = status;

        ProjectCompatibilityData data = Run(Project("Line1"));

        Assert.Equal((status, compatible, "18.0", false), (data.Status, data.Compatible, data.DatabaseVersion, data.Open));
    }

    [Fact]
    public void Compatibility_OfAnOpenProject_SaysSo() => Assert.True(Run(Project("Open")).Open);

    [Theory]
    [InlineData(@"relative\Line1.elk")]
    [InlineData(@"\server\share\Line1.elk")]
    [InlineData(@"C:\scratch\Line1.edb")]
    [InlineData("")]
    public void Compatibility_OfNoProjectFilePath_IsInvalidArgument(string path) =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Run(path)).Error.Reason);

    [Fact]
    public void Compatibility_OfAMissingFile_IsFileNotFound_NamingOnlyTheFile()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Run(Path.Combine(_root, "Gone.elk")));

        Assert.Equal((ErrorReason.FileNotFound, "Gone.elk"), (error.Error.Reason, error.Error.Metadata!["file"]));
        Assert.DoesNotContain(_root, error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string Project(string name)
    {
        string link = Path.Combine(_root, name + ".elk");
        File.WriteAllText(link, string.Empty);
        return link;
    }

    private ProjectCompatibilityData Run(string path) =>
        (ProjectCompatibilityData)_operation.Execute(_operation.ParseArgs(JObject.FromObject(new ProjectCompatibilityArgs { Path = path })))!;
}
