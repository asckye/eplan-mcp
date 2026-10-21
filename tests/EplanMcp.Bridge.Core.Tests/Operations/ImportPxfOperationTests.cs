using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// import.pxf.plan and import.pxf.execute against an EPJ file and a scratch folder in a temporary directory
/// (docs/tools/eplan_import_pxf.md, sections 2, 4 and 5): the plan is one new project whose path is in scratch, a
/// project of the name already there is ALREADY_EXISTS, and a failed or cancelled import leaves no files behind, or
/// says that it may have. The importer makes the files S22 found (A29): the .elk and the .edb folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImportPxfOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-import-pxf-").FullName;
    private readonly string _file;
    private readonly string _imported;
    private readonly Importer _importer = new();

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

    [Fact]
    public void Execute_ImportsTheFileAtTheTarget_AndReturnsTheNewProject()
    {
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;

        ImportPxfData data = Execute(args);

        string target = Path.Combine(_imported, "Line1_imported");
        Assert.Equal(("Line1_imported", target + ".elk"), (data.Name, data.Path));
        Assert.InRange(data.Seconds, 0, 60);
        Assert.Equal((_file, target), Assert.Single(_importer.Imports));
    }

    [Fact]
    public void Execute_WhatEPLANRefuses_DeletesWhatTheImportMade_AndIsRolledBack()
    {
        _importer.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not import the EPJ file as a project.");
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.EndsWith("Any files the import had made were deleted again.", error.Error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_imported));
    }

    [Fact]
    public void Execute_AnImportThatMadeNoProject_IsEplanOperationFailed()
    {
        _importer.Make = target => Directory.CreateDirectory(target + ".edb");
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_imported));
    }

    [Fact]
    public void Execute_ACancelDuringTheImport_TakesTheNewProjectAwayAgain()
    {
        var context = new OperationContext("r1", write: true, progress: null);
        _importer.Make += _ => context.Cancel();
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => Execute(args)));

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_imported));
    }

    [Fact]
    public void Execute_FilesThatCannotBeDeleted_MakeTheOutcomeUnknown()
    {
        _importer.Failure = new InvalidOperationException("EPLAN stopped.");
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;
        FileStream? held = null;
        _importer.Make += target => held = new FileStream(Path.Combine(target + ".edb", "Project.db"), FileMode.Open, FileAccess.Read, FileShare.None);

        try
        {
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

            Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Error.Reason, error.Error.Execution));
            Assert.Contains("incomplete project", error.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Fact]
    public void Execute_AFileRewrittenAfterThePlan_IsPlanStale_AndImportsNothing()
    {
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;
        File.SetLastWriteTimeUtc(_file, File.GetLastWriteTimeUtc(_file).AddMinutes(1));

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(_importer.Imports);
    }

    [Fact]
    public void Execute_AProjectOfTheNameMadeSinceThePlan_IsAlreadyExists_AndStaysAsItIs()
    {
        ImportPxfArgs args = Args("Line1_imported");
        args.Fingerprint = Plan(args).Fingerprint;
        string theirs = Path.Combine(_imported, "Line1_imported.elk");
        File.WriteAllText(theirs, "someone else's");

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Equal("someone else's", File.ReadAllText(theirs));
        Assert.Empty(_importer.Imports);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_IsInvalid_AndNotStarted()
    {
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1_imported")));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    private ImportPxfArgs Args(string name) =>
        new() { File = "incoming/Line1.epj", FilePath = _file, Sha256 = Sha256(), Name = name, Target = Path.Combine(_imported, name) };

    private string Sha256() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_file)));

    private static WritePlan Plan(ImportPxfArgs args) => (WritePlan)new ImportPxfPlanOperation().Execute(args)!;

    private ImportPxfData Execute(ImportPxfArgs args) => (ImportPxfData)new ImportPxfExecuteOperation(_importer).Execute(args)!;

    /// <summary>
    /// <c>Import.PXFProject</c> as S22 saw it (A29): <see cref="Make"/> makes the new project's files at the target, then
    /// <see cref="Failure"/>, if any, is thrown, as an import EPLAN gives up on leaves what it made.
    /// </summary>
    private sealed class Importer : IProjectImporter
    {
        public Action<string>? Make { get; set; } = target =>
        {
            File.WriteAllText(target + ".elk", "link");
            Directory.CreateDirectory(Path.Combine(target + ".edb", "Pages"));
            File.WriteAllText(Path.Combine(target + ".edb", "Project.db"), "data");
        };

        public Exception? Failure { get; set; }

        public List<(string File, string Target)> Imports { get; } = [];

        public void ImportPxf(string filePath, string target)
        {
            Imports.Add((filePath, target));
            Make?.Invoke(target);
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
