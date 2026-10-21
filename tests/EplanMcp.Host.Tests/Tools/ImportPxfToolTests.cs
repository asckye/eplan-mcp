using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_import_pxf through the real write pipeline against a fake Bridge, with an EPJ file in the harness's import folder
/// and its scratch root (docs/tools/eplan_import_pxf.md, sections 2 to 6): the new project goes to
/// <c>&lt;scratch&gt;\imported\&lt;name&gt;</c>, which the policy finds in scratch, so nobody is asked.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImportPxfToolTests : IDisposable
{
    private const string File = "incoming/Line1.epj";

    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private readonly ImportFiles _files;
    private readonly HostConfig _config;
    private readonly string _target;

    public ImportPxfToolTests()
    {
        _files = new ImportFiles(_harness.Locations);
        _config = new HostConfig([_harness.Locations.DefaultScratch], OutsideScratchWrites.Deny);
        string path = Path.Combine(_harness.Locations.Imports, "incoming", "Line1.epj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[4096]);
        _target = Path.Combine(_harness.Locations.DefaultScratch, "imported", "Line1_imported");
        _harness.Bridge.Handlers[OperationNames.ImportPxfPlan] = args =>
        {
            var import = (ImportPxfArgs)args!;
            return new WritePlan
            {
                Project = import.Name!,
                ProjectPath = import.Target + ".elk",
                Changes = [new PlannedChange { Action = "create", Kind = "project", Name = import.Name!, NewValue = "Line1.epj (4 KB, sha256 …) into the scratch folder's 'imported' folder" }],
                Fingerprint = "f1",
            };
        };
        _harness.Bridge.Handlers[OperationNames.ImportPxfExecute] = args => new ImportPxfData { Name = ((ImportPxfArgs)args!).Name!, Path = ((ImportPxfArgs)args!).Target + ".elk", Seconds = 58 };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task AnImport_MakesTheProjectInScratch_WithoutAsking_AndSaysItIsNotOpened()
    {
        CallToolResult result = await Import("Line1_imported");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", File, 4096L, 58), (data.GetProperty("execution").GetString(), data.GetProperty("file").GetString(), data.GetProperty("bytes").GetInt64(), data.GetProperty("seconds").GetInt32()));
        JsonElement project = data.GetProperty("project");
        Assert.Equal(("Line1_imported", _target + ".elk", false), (project.GetProperty("name").GetString(), project.GetProperty("path").GetString(), project.GetProperty("opened").GetBoolean()));
        Assert.Equal("The project is not opened; open it in EPLAN to work on it.", data.GetProperty("hint").GetString());
        Assert.Equal("Imported the EPJ file as a new project in scratch; it is not opened yet.", WriteToolHarness.Text(result, 0));
        var executed = (ImportPxfArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.ImportPxfExecute).Args!;
        Assert.Equal((File, "Line1_imported", _target, "f1"), (executed.File, executed.Name, executed.Target, executed.Fingerprint));
        Assert.Equal(64, executed.Sha256!.Length);
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.True(Directory.Exists(Path.GetDirectoryName(_target)));
    }

    [Fact]
    public async Task ADryRun_SaysWhereTheProjectWouldBe_AndImportsNothing()
    {
        CallToolResult result = await Import("Line1_imported", dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", _target + ".elk", JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("project").GetProperty("path").GetString(), data.GetProperty("seconds").ValueKind));
        Assert.Equal("Dry run: would import the EPJ file as a new project in scratch. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("", "'name' is required")]
    [InlineData("Line1.v2", "'name' must not contain '.'")]
    [InlineData("sub/Line1", "'name' must be a name only")]
    [InlineData("CON", "'name' must not be a name Windows reserves")]
    public async Task AnInvalidName_IsReportedWithTheFile_AndTheBridgeIsNotAsked(string name, string violation)
    {
        JsonElement error = WriteToolHarness.Error(await ImportTools.ImportPxfAsync(_harness.Pipeline, _files, _config, "../Line1.epj", name, cancellationToken: TestContext.Current.CancellationToken));

        string[] violations = error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n');
        Assert.Equal(2, violations.Length);
        Assert.StartsWith(violation, violations[1], StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public void ANameOf60Characters_IsFine_And61AreNot()
    {
        Assert.Null(ImportTools.ProjectNameProblem(new string('a', 60)));
        Assert.Equal("must have at most 60 characters.", ImportTools.ProjectNameProblem(new string('a', 61)));
    }

    [Fact]
    public async Task AMissingFile_IsFileNotFound()
    {
        JsonElement error = WriteToolHarness.Error(await ImportTools.ImportPxfAsync(_harness.Pipeline, _files, _config, "incoming/Other.epj", "Other", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public async Task AnImportedFolderThatIsAJunction_IsPolicyDenied()
    {
        string elsewhere = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "elsewhere")).FullName;
        Directory.CreateDirectory(_harness.Locations.DefaultScratch);
        string link = Path.Combine(_harness.Locations.DefaultScratch, "imported");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{elsewhere}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }

        try
        {
            JsonElement error = WriteToolHarness.Error(await Import("Line1_imported"));

            Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Equal(0, _harness.Bridge.Invocations);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.OutcomeUnknown, ExecutionState.Unknown)]
    [InlineData(ErrorReason.AlreadyExists, ExecutionState.NotStarted)]
    public async Task AnExecutionError_ReachesTheModelWithItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Import("Line1_imported"));

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    private Task<CallToolResult> Import(string name, bool dryRun = false) =>
        ImportTools.ImportPxfAsync(_harness.Pipeline, _files, _config, File, name, dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
