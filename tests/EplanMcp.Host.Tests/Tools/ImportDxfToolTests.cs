using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_import_dxf through the real write pipeline against a fake Bridge, with a drawing in the harness's import folder
/// (docs/tools/eplan_import_dxf.md, sections 2 to 4 and 7; SEC-31). The commit reads back what S22 found for a Rittal
/// drawing (api-notes A29): 227 objects.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImportDxfToolTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/92";
    private const string File = "drawings/4685200F.DXF";

    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);
    private readonly ImportFiles _files;
    private readonly string _path;
    private readonly string _sha256;

    public ImportDxfToolTests()
    {
        _files = new ImportFiles(_harness.Locations);
        _path = Path.Combine(_harness.Locations.Imports, "drawings", "4685200F.DXF");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] content = [.. Enumerable.Range(0, 25 * 1024).Select(i => (byte)i)];
        System.IO.File.WriteAllBytes(_path, content);
        _sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        _harness.Bridge.Handlers[OperationNames.ImportDxfPlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "drawing", Name = Page, NewValue = $"4685200F.DXF (25 KB, sha256 {_sha256[..12]}…), scale fit, center" }]);
            plan.Details = new Dictionary<string, string> { ["file"] = File, ["sha256"] = _sha256, ["bytes"] = "25600", ["scale"] = "fit", ["position"] = "center" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ImportDxfExecute] = _ => new ImportDxfData { Project = "Line1", Page = Page, File = File, Sha256 = _sha256, ObjectsAdded = 227 };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_NamesTheFileItsSizeAndSha256_AndChangesNothing()
    {
        CallToolResult result = await Import(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, Page, File, _sha256, 25600L), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("page").GetString(), data.GetProperty("file").GetString(), data.GetProperty("sha256").GetString(), data.GetProperty("bytes").GetInt64()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("objectsAdded").ValueKind, data.GetProperty("undo").ValueKind));
        Assert.Equal("Dry run: would import one drawing. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ACommit_SaysHowManyObjectsTheDrawingAdded_AndPassesTheFileTheHostRead()
    {
        CallToolResult result = await Import(scale: "none", position: "lower_left");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_import_dxf", 227), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("objectsAdded").GetInt32()));
        Assert.Equal("Imported a drawing with 227 objects; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var planned = (ImportDxfArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.ImportDxfPlan).Args!;
        var executed = (ImportDxfArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.ImportDxfExecute).Args!;
        Assert.Equal((Page, File, _path, _sha256, "none", "lower_left", (string?)null), (planned.Page, planned.File, planned.FilePath, planned.Sha256, planned.Scale, planned.Position, planned.Fingerprint));
        Assert.Equal((_path, _sha256, "f1"), (executed.FilePath, executed.Sha256, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task TheAudit_RecordsTheFileAndItsSha256()
    {
        await Import();

        string audit = string.Join("\n", Directory.EnumerateFiles(_harness.Locations.Audit).Select(System.IO.File.ReadAllText));
        Assert.Contains("\"file\":\"drawings/4685200F.DXF\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"sha256\":\"" + _sha256 + "\"", audit, StringComparison.Ordinal);
        Assert.Contains("create drawing =GAA+A1&EFS1/92 (4685200F.DXF (25 KB, sha256 " + _sha256[..12], audit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingFile_IsFileNotFound_AndTheBridgeIsNotAsked()
    {
        JsonElement error = WriteToolHarness.Error(await ImportTools.ImportDxfAsync(_harness.Pipeline, _files, "Line1", Page, "drawings/missing.dxf", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted, "drawings/missing.dxf"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("file").GetString()));
        Assert.DoesNotContain(_harness.Locations.Root, error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public async Task APathOutsideTheImportFolder_AndAMissingProject_AreReportedTogether()
    {
        JsonElement error = WriteToolHarness.Error(await ImportTools.ImportDxfAsync(_harness.Pipeline, _files, " ", Page, @"..\..\secret.dxf", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        string[] violations = error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n');
        Assert.Equal(2, violations.Length);
        Assert.StartsWith("'file' must be a path below the 'imports' folder", violations[1], StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public async Task ARecoveredCommit_PointsToThePage()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Import();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Contains("eplan_export_image", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Imported a drawing; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Theory]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.FileNotFound, ExecutionState.NotStarted)]
    public async Task AnExecutionError_ReachesTheModelWithItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Import());

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_WithApproval_AsksWithTheFileSizeAndSha256()
    {
        var channel = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        string path = Path.Combine(harness.Locations.Imports, "plan.dxf");
        Directory.CreateDirectory(harness.Locations.Imports);
        System.IO.File.WriteAllBytes(path, [1, 2, 3]);
        harness.Bridge.Handlers[OperationNames.ImportDxfPlan] = _ => harness.Plan([new PlannedChange { Action = "create", Kind = "drawing", Name = Page, NewValue = "plan.dxf (3 bytes, sha256 039058c6f2c0…), scale fit, center" }]);
        harness.Bridge.Handlers[OperationNames.ImportDxfExecute] = _ => new ImportDxfData { Project = "Line1", Page = Page, ObjectsAdded = 3 };

        JsonElement data = WriteToolHarness.Structured(await ImportTools.ImportDxfAsync(harness.Pipeline, new ImportFiles(harness.Locations), "Line1", Page, "plan.dxf", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Equal(
            "Import a drawing on page =GAA+A1&EFS1/92: plan.dxf (3 bytes, sha256 039058c6f2c0…), scale fit, center",
            ApprovalTexts.Change(Assert.Single(Assert.Single(channel.Questions).Plan.Changes), zh: false));
    }

    private Task<CallToolResult> Import(bool dryRun = false, string scale = "fit", string position = "center") =>
        ImportTools.ImportDxfAsync(_harness.Pipeline, _files, "Line1", Page, File, scale, position, dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
