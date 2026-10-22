using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// part.import.plan (docs/tools/eplan_part_import.md): the file's part numbers classified by the mode against the
/// configured database, a plan without project that names the database, the fingerprint of the file and the parts it
/// finds, and no plan on SQL Server. Real files; the database is a fake.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartImportOperationTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-partimport-").FullName;
    private readonly FakePartsDatabaseWriter _writer = new();
    private readonly string _file;

    public PartImportOperationTests()
    {
        // SIE.3RH2122-1HB40 is in the database already; the other two are new, one with two variants.
        _file = Write("""
            <partsmanagement>
             <part P_ARTICLE_PARTNR="SIE.3RH2122-1HB40"><variant P_ARTICLE_VARIANT="1" /></part>
             <part P_ARTICLE_PARTNR="EPLANMCP.LARGE-TEST"><variant P_ARTICLE_VARIANT="1" /><variant P_ARTICLE_VARIANT="2" /></part>
             <part P_ARTICLE_PARTNR="PXC.1234567"><variant P_ARTICLE_VARIANT="1" /></part>
            </partsmanagement>
            """);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(null, "EPLANMCP.LARGE-TEST,PXC.1234567", "", "SIE.3RH2122-1HB40")]
    [InlineData("update_existing", "", "SIE.3RH2122-1HB40", "EPLANMCP.LARGE-TEST,PXC.1234567")]
    [InlineData("update_and_append", "EPLANMCP.LARGE-TEST,PXC.1234567", "SIE.3RH2122-1HB40", "")]
    [InlineData("replace_and_append", "EPLANMCP.LARGE-TEST,PXC.1234567", "SIE.3RH2122-1HB40", "")]
    public void ThePlan_ClassifiesThePartsByTheMode_AndNamesTheDatabase(string? mode, string added, string updated, string skipped)
    {
        WritePlan plan = Plan(Args(mode: mode));

        Assert.Equal((string.Empty, string.Empty), (plan.Project, plan.ProjectPath));
        Assert.Equal((MasterDataKinds.Parts, @"$(MD_PARTS)\EPLAN_parts.alk", @"C:\Parts\EPLAN_parts.alk", false), (plan.MasterData!.Kind, plan.MasterData.Database, plan.MasterData.Path, plan.MasterData.Server));
        Assert.Equal(Lines(added), plan.Details![PartImportPlanOperation.Added]);
        Assert.Equal(Lines(updated), plan.Details[PartImportPlanOperation.Updated]);
        Assert.Equal(Lines(skipped), plan.Details[PartImportPlanOperation.Skipped]);
        Assert.Equal("3", plan.Details[PartImportPlanOperation.PartsBefore]);
        Assert.Equal(mode ?? "append_new", plan.Details[PartImportPlanOperation.Mode]);
        Assert.StartsWith("relays.xml (", plan.Details[PartImportPlanOperation.File], StringComparison.Ordinal);
        Assert.Equal(
            [.. added.Length > 0 ? new[] { ("create", "2") } : [], .. updated.Length > 0 ? new[] { ("update", "1") } : []],
            plan.Changes.Select(c => (c.Action, c.NewValue!)));
        Assert.All(plan.Changes, c => Assert.Equal((PartImportPlanOperation.Parts, PartImportPlanOperation.Parts, "relays.xml"), (c.Kind, c.Property, c.Name)));
        Assert.Equal(plan.Fingerprint, Plan(Args(mode: mode)).Fingerprint);
    }

    [Fact]
    public void TheFingerprint_ChangesWhenTheDatabaseGainsOneOfTheFilesParts_NotAnother()
    {
        string before = Plan(Args()).Fingerprint;
        _writer.Parts["SOMETHING.ELSE"] = 1;
        string other = Plan(Args()).Fingerprint;
        _writer.Parts["PXC.1234567"] = 1;

        Assert.Equal(before, other);
        Assert.NotEqual(before, Plan(Args()).Fingerprint);
    }

    [Fact]
    public void AFileWhosePartsTheModeAllSkips_PlansNoChange()
    {
        WritePlan plan = Plan(Args(file: Write("""<partsmanagement><part P_ARTICLE_PARTNR="PXC.3209510" /></partsmanagement>""")));

        Assert.Empty(plan.Changes);
        Assert.Equal(("PXC.3209510", "1"), (plan.Details![PartImportPlanOperation.Skipped], plan.Details[PartImportPlanOperation.SkippedCount]));
    }

    [Fact]
    public void ADatabaseOnSqlServer_IsRefusedByPolicy()
    {
        _writer.Database = new ConfiguredDatabase { Database = "SRV01|EPLAN_Parts|2|eplan|secret" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args())).Error;

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, _writer.Reads);
    }

    [Fact]
    public void EveryArgumentProblem_IsReportedAtOnce()
    {
        var args = new PartImportArgs { File = "relays.csv", FilePath = Path.Combine(_folder, "relays.csv"), Sha256 = "ABC", Mode = "merge", MatchBy = "guid" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(4, error.Metadata!["violations"].Split('\n').Length);
    }

    [Fact]
    public void AFileThatChangedSinceTheHostReadIt_IsStale()
    {
        PartImportArgs args = Args();
        File.AppendAllText(_file, "<!-- changed -->");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;

        Assert.Equal(ErrorReason.PlanStale, error.Reason);
    }

    private static string Lines(string csv) => csv.Replace(',', '\n');

    private PartImportArgs Args(string? file = null, string? mode = null, string? fingerprint = null)
    {
        string path = file ?? _file;
        return new PartImportArgs
        {
            File = file is null ? "relays.xml" : Path.GetFileName(path),
            FilePath = path,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            Mode = mode,
            Fingerprint = fingerprint,
        };
    }

    private WritePlan Plan(PartImportArgs args) => (WritePlan)new PartImportPlanOperation(_writer).Execute(args)!;


    private string Write(string content)
    {
        string file = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file, content);
        return file;
    }
}
