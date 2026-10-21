using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// import.pxf.plan against an EPJ file and a scratch folder in a temporary directory (docs/tools/eplan_import_pxf.md,
/// sections 2, 4 and 5): the plan is one new project whose path is in scratch, and a project of the name already there
/// is ALREADY_EXISTS.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImportPxfOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-import-pxf-").FullName;
    private readonly string _file;
    private readonly string _imported;

    public ImportPxfOperationTests()
    {
        _file = Path.Combine(_root, "imports", "incoming", "Line1.epj");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, new byte[3 * 1024 * 1024]);
        _imported = Directory.CreateDirectory(Path.Combine(_root, "scratch", "imported")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_IsOneNewProject_WhosePathIsTheTargetsLinkFile()
    {
        WritePlan plan = Plan(Args("Line1_imported"));

        Assert.Equal(("Line1_imported", Path.Combine(_imported, "Line1_imported.elk")), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "project", "Line1_imported"), (change.Action, change.Kind, change.Name));
        Assert.Equal($"Line1.epj (3 MB, sha256 {Sha256()[..12]}…) into the scratch folder's 'imported' folder", change.NewValue);
        Assert.Equal(("incoming/Line1.epj", Sha256(), "3145728", plan.ProjectPath), (plan.Details!["file"], plan.Details["sha256"], plan.Details["bytes"], plan.Details["path"]));
    }

    [Theory]
    [InlineData("Line1_imported.elk")]
    [InlineData("Line1_imported.ell")]
    [InlineData("Line1_imported.edb")]
    public void Plan_AProjectOfTheNameInScratch_IsAlreadyExists(string existing)
    {
        string path = Path.Combine(_imported, existing);
        if (existing.EndsWith(".edb", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            File.WriteAllText(path, string.Empty);
        }

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(Args("Line1_imported")));

        Assert.Equal((ErrorReason.AlreadyExists, existing), (error.Error.Reason, error.Error.Metadata!["existing"]));
        Assert.Contains("another 'name'", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ReportsEveryArgumentProblemAtOnce()
    {
        var args = new ImportPxfArgs { File = "incoming/Line1.zip", FilePath = Path.ChangeExtension(_file, ".zip"), Sha256 = Sha256(), Name = " " };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.Equal(
            ["'filePath' must be the full path the Host resolved below its import folder, ending with .epj or .pxf.", "'name' is required: the new project's name."],
            error.Error.Metadata!["violations"].Split('\n'));
    }

    [Theory]
    [InlineData("Other")]
    [InlineData("missing\\Line1_imported")]
    public void Plan_ATargetThatIsNotTheNameInAnExistingFolder_IsInvalid(string target)
    {
        ImportPxfArgs args = Args("Line1_imported");
        args.Target = Path.Combine(_imported, target);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(args));

        Assert.StartsWith("'target' must be the new project's full path", error.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AFileGoneSinceTheHostReadIt_IsFileNotFound()
    {
        ImportPxfArgs args = Args("Line1_imported");
        File.Delete(_file);

        Assert.Equal(ErrorReason.FileNotFound, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_DoesNotReadTheFileAgain_SoItsFingerprintFollowsSizeAndTime()
    {
        // Up to 2 GB: the plan takes the Host's SHA-256 and only the file's size and time (tool spec 2).
        ImportPxfArgs args = Args("Line1_imported");
        args.Sha256 = new string('0', 64);
        string first = Plan(args).Fingerprint;
        File.SetLastWriteTimeUtc(_file, File.GetLastWriteTimeUtc(_file).AddMinutes(1));

        Assert.NotEqual(first, Plan(args).Fingerprint);
    }

    private ImportPxfArgs Args(string name) =>
        new() { File = "incoming/Line1.epj", FilePath = _file, Sha256 = Sha256(), Name = name, Target = Path.Combine(_imported, name) };

    private string Sha256() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_file)));

    private static WritePlan Plan(ImportPxfArgs args) => (WritePlan)new ImportPxfPlanOperation().Execute(args)!;
}
