using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The master-data backup before a master-data write (ADR-0019): only the planned file database, only into the Host's
/// new, empty folder, and only one non-empty 7z archive counts as a backup; every failure is not_started. Real folders.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MasterDataBackupOperationTests : IDisposable
{
    private const string Database = @"$(MD_PARTS)\EPLAN_parts.alk";

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-mdbackup-").FullName;
    private readonly FakeMasterDataBackup _backup = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheArchiveInTheFolder_IsTheBackup()
    {
        MasterDataBackupData data = Run(Args());

        string archive = Path.Combine(_folder, "parts.zw6");
        Assert.Equal([(Database, _folder, "parts")], _backup.Backups);
        Assert.Equal((Database, archive, (long)FakeMasterDataBackup.SevenZip.Length), (data.Database, data.Archive, data.Bytes));
    }

    [Fact]
    public void ASqlServerDatabase_IsRefusedByPolicy_WithoutABackup()
    {
        _backup.Database = "SRV01|EPLAN_Parts|2|eplan|secret";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(Args(database: _backup.Database))).Error;

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Empty(_backup.Backups);
    }

    [Fact]
    public void AnotherConfiguredDatabase_IsStale_WithoutABackup()
    {
        _backup.Database = @"$(MD_PARTS)\Other.alk";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(Args())).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Backups);
    }

    [Fact]
    public void AnotherKind_IsAnInvalidArgument()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(new MasterDataBackupArgs { Kind = "translation", Database = Database, Folder = _folder, ArchiveName = "parts", MaxSeconds = 60 })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Backups);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("unc")]
    [InlineData("dotdot")]
    [InlineData("no folder")]
    [InlineData("folder not empty")]
    [InlineData("archive name with path")]
    [InlineData("no time")]
    public void WithoutANewEmptyFolder_NothingIsBackedUp(string problem)
    {
        MasterDataBackupArgs args = problem switch
        {
            "relative" => Args(folder: @"backups\masterdata"),
            "unc" => Args(folder: @"\\server\share\masterdata"),
            "dotdot" => Args(folder: Path.Combine(_folder, "..", Path.GetFileName(_folder))),
            "no folder" => Args(folder: Path.Combine(_folder, "gone")),
            "archive name with path" => Args(archiveName: @"..\parts"),
            "no time" => Args(maxSeconds: 0),
            _ => Args(),
        };
        if (problem == "folder not empty")
        {
            File.WriteAllText(Path.Combine(_folder, "earlier.zw6"), "an earlier backup");
        }

        BridgeError error = Fails(args);

        Assert.Empty(_backup.Backups);
        Assert.Equal("The write came without a new, empty backup folder.", error.Metadata!["problem"]);
    }

    [Fact]
    public void ABackupThatFails_IsNotStarted()
    {
        _backup.Failure = new InvalidOperationException("EPLAN's backup action returned false.");

        BridgeError error = Fails(Args());

        Assert.Equal("EPLAN could not back the parts database up.", error.Metadata!["problem"]);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("empty")]
    [InlineData("not 7z")]
    [InlineData("other extension")]
    [InlineData("two files")]
    public void AnythingButOneArchive_IsNoBackup(string left)
    {
        _backup.Write = left switch
        {
            "nothing" => LeaveNothing,
            "empty" => (folder, name) => File.WriteAllBytes(Path.Combine(folder, name + ".zw6"), []),
            "not 7z" => (folder, name) => File.WriteAllText(Path.Combine(folder, name + ".zw6"), "PK zip, not 7z"),
            "other extension" => (folder, name) => File.WriteAllBytes(Path.Combine(folder, name + ".zw7"), FakeMasterDataBackup.SevenZip),
            _ => TwoFiles,
        };

        BridgeError error = Fails(Args());

        Assert.NotNull(error.Metadata!["problem"]);
    }

    [Fact]
    public void ABackupOverItsLimit_IsNoBackup()
    {
        _backup.Delay = TimeSpan.FromSeconds(1.1);

        BridgeError error = Fails(Args(maxSeconds: 1));

        Assert.StartsWith("The backup took 1 s, more than the 1 s", error.Metadata!["problem"], StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorTheAdapterNames_StaysButIsNotStarted()
    {
        _backup.Failure = new BridgeErrorException(ErrorReason.EplanBusy, "busy", retryable: true);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(Args())).Error;

        Assert.Equal((ErrorReason.EplanBusy, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    private static void LeaveNothing(string folder, string name)
    {
    }

    private static void TwoFiles(string folder, string name)
    {
        File.WriteAllBytes(Path.Combine(folder, name + ".zw6"), FakeMasterDataBackup.SevenZip);
        File.WriteAllText(Path.Combine(folder, "log.txt"), "?");
    }

    private MasterDataBackupArgs Args(string? folder = null, string archiveName = "parts", int maxSeconds = 60, string database = Database) =>
        new() { Kind = MasterDataKinds.Parts, Database = database, Folder = folder ?? _folder, ArchiveName = archiveName, MaxSeconds = maxSeconds };

    private MasterDataBackupData Run(MasterDataBackupArgs args) => (MasterDataBackupData)new MasterDataBackupOperation(_backup).Execute(args)!;

    private BridgeError Fails(MasterDataBackupArgs args)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, MasterDataBackupOperation.FailedMessage), (error.Reason, error.Execution, error.Message));
        return error;
    }
}
