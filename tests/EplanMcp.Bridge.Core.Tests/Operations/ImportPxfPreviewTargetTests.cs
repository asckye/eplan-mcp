using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ImportPxfPreviewTargetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-pxf-preview-").FullName;
    private readonly ImportPxfArgs _args;
    private readonly Importer _importer = new();

    public ImportPxfPreviewTargetTests()
    {
        string source = Path.Combine(_root, "incoming", "Line1.epj");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, [1, 2, 3]);
        _args = new ImportPxfArgs
        {
            File = "incoming/Line1.epj",
            FilePath = source,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))),
            Name = "Line1_imported",
            Target = Path.Combine(_root, "scratch", "imported", "Line1_imported"),
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_MissingTargetParent_ReturnsFullPathAndChangesWithoutCreatingIt()
    {
        byte[] before = File.ReadAllBytes(_args.FilePath!);
        WritePlan plan = (WritePlan)new ImportPxfPlanOperation().Execute(_args)!;

        Assert.Equal(_args.Target + ".elk", plan.ProjectPath);
        Assert.True(Path.IsPathFullyQualified(plan.ProjectPath));
        Assert.Equal("Line1_imported", Assert.Single(plan.Changes).Name);
        Assert.NotEmpty(plan.Fingerprint);
        Assert.False(Directory.Exists(Path.GetDirectoryName(_args.Target)));
        Assert.Equal(before, File.ReadAllBytes(_args.FilePath!));

        _args.Fingerprint = plan.Fingerprint;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new ImportPxfExecuteOperation(_importer).Execute(_args));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(0, _importer.Calls);
        Assert.False(Directory.Exists(Path.GetDirectoryName(_args.Target)));
    }

    [Fact]
    public void Execute_AfterHostCreatesTargetParent_UsesTheSamePlanFingerprint()
    {
        WritePlan plan = (WritePlan)new ImportPxfPlanOperation().Execute(_args)!;
        _args.Fingerprint = plan.Fingerprint;
        Directory.CreateDirectory(Path.GetDirectoryName(_args.Target)!);

        ImportPxfData result = (ImportPxfData)new ImportPxfExecuteOperation(_importer).Execute(_args)!;

        Assert.Equal(_args.Target + ".elk", result.Path);
        Assert.Equal(1, _importer.Calls);
    }

    private sealed class Importer : IProjectImporter
    {
        public int Calls { get; private set; }

        public void ImportPxf(string filePath, string target)
        {
            Calls++;
            File.WriteAllText(target + ".elk", "link");
            Directory.CreateDirectory(target + ".edb");
        }
    }
}
