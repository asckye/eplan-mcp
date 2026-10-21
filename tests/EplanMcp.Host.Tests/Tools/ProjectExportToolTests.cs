using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// The exports of a whole project on a fake Bridge that writes into the folder the Host created, a real one in a
/// temporary data directory (docs/tools/eplan_export_pxf.md, eplan_export_parts.md, sections 2 to 5). The steps they share with
/// eplan_export_pdf, failures and cancels included, are <see cref="EplanMcp.Host.Tests.Exports.ExportRunnerTests"/>'s.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectExportToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-project-export-tools-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();
    private readonly RecordedProgress _progress = new();
    private long? _free = 100L * 1024 * 1024 * 1024;
    private readonly ExportRunner _runner;

    public ProjectExportToolTests()
    {
        _runner = new ExportRunner(_bridge, new ExportFolders(_locations, _time) { FreeSpace = _ => _free }, _time, NullLogger<ExportRunner>.Instance);
        _bridge.Handlers[OperationNames.ExportPxfPlan] = _ => new ExportPlanData { Project = "Line1" };
        _bridge.Handlers[OperationNames.ExportPxfRun] = args =>
        {
            // As S25 found EPLAN writes it: the extension added, and an empty folder named like the file.
            var run = (ExportArgs)args!;
            File.WriteAllBytes(Path.Combine(run.Directory!, run.FileName + ".epj"), new byte[4096]);
            Directory.CreateDirectory(Path.Combine(run.Directory!, run.FileName!));
            return new ExportRunData { Project = "Line1" };
        };
        _bridge.Handlers[OperationNames.ExportPartsPlan] = _ => new ExportPlanData { Project = "Line1" };
        _bridge.Handlers[OperationNames.ExportPartsRun] = args =>
        {
            var run = (ExportArgs)args!;
            File.WriteAllBytes(Path.Combine(run.Directory!, run.FileName!), new byte[13 * 1024 * 1024]);
            return new ExportRunData { Project = "Line1" };
        };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task Pxf_IsOneEpjFileInANewFolder_NamedAfterTheProject()
    {
        CallToolResult result = await Pxf();

        JsonElement data = Structured(result);
        string directory = data.GetProperty("directory").GetString()!;
        Assert.StartsWith(Path.Combine(_locations.Exports, "Line1", "20261021T083000Z-"), directory, StringComparison.Ordinal);
        Assert.Equal(("Line1", false), (data.GetProperty("project").GetString(), data.GetProperty("masterData").GetBoolean()));
        Assert.Equal(("Line1.epj", 4096L, 1, 4096L, false), (Files(data)[0].Path, Files(data)[0].Bytes, data.GetProperty("fileCount").GetInt32(), data.GetProperty("totalBytes").GetInt64(), data.GetProperty("truncated").GetBoolean()));
        Assert.Equal("Exported the project to a 4 KB EPJ file in a new folder; see 'directory'.", Text(result, 0));
        Assert.Equal(["project", "masterData", "directory", "files", "fileCount", "totalBytes", "truncated", "hint"], data.EnumerateObject().Select(p => p.Name));

        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPxfRun).Args!;
        Assert.Equal((directory, "Line1", false), (run.Directory, run.FileName, run.IncludeMasterData));
        Assert.Equal([ExportRunner.Timeout], _bridge.Timeouts);
    }

    [Theory]
    [InlineData("Line1 archive", "Line1 archive")]
    [InlineData("Line1 archive.epj", "Line1 archive")]
    [InlineData("Line1.EPJ", "Line1")]
    public async Task Pxf_TakesTheFileNameWithoutTheExtension_AndTheMasterData(string fileName, string sent)
    {
        JsonElement data = Structured(await Pxf(project: "Line1", masterData: true, fileName: fileName));

        var plan = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPxfPlan).Args!;
        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPxfRun).Args!;
        Assert.Equal(("Line1", true), (plan.Project, plan.IncludeMasterData));
        Assert.Equal((sent, true), (run.FileName, run.IncludeMasterData));
        Assert.True(data.GetProperty("masterData").GetBoolean());
    }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData("C:x.epj")]
    [InlineData("x .epj")]
    [InlineData("CON.epj")]
    public async Task Pxf_AnInvalidFileName_IsRefusedBeforeAnythingIsCreated(string fileName)
    {
        JsonElement error = Error(await Pxf(fileName: fileName));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.StartsWith("'file_name' ", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task Pxf_WithLessThan2GbFree_CallsNoExportAndCreatesNoFolder()
    {
        _free = ProjectExportTools.PxfFreeBytes - 1;

        JsonElement error = Error(await Pxf());

        Assert.Equal(ErrorReason.EplanOperationFailed, error.GetProperty("reason").GetString());
        Assert.Contains("is free on the drive of the server's export folder, less than the 2 GB", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_locations.Root, error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal([OperationNames.ExportPxfPlan], _bridge.Calls.Select(c => c.Operation));
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task Pxf_WhenTheFreeSpaceIsUnknown_Exports()
    {
        _free = null;

        Structured(await Pxf());
    }

    [Theory]
    [InlineData(@"Line1\leftover.txt")]
    [InlineData("Line1.log")]
    public async Task Pxf_AnythingButOneEpjFile_FailsTheExport_AndKeepsNoFolder(string extra)
    {
        Func<object?, object> export = _bridge.Handlers[OperationNames.ExportPxfRun];
        _bridge.Handlers[OperationNames.ExportPxfRun] = args =>
        {
            object data = export(args);
            File.WriteAllText(Path.Combine(((ExportArgs)args!).Directory!, extra), "x");
            return data;
        };

        JsonElement error = Error(await Pxf());

        Assert.Equal(ErrorReason.EplanOperationFailed, error.GetProperty("reason").GetString());
        Assert.Contains("other files than one .epj file", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_locations.Exports, "Line1")));
    }

    [Fact]
    public async Task Pxf_ATimeout_SaysWhatToDoWithoutPages()
    {
        _bridge.Handlers[OperationNames.ExportPxfRun] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.Timeout, Message = "m", Retryable = true });

        string message = Error(await Pxf()).GetProperty("message").GetString()!;

        Assert.Contains("did not finish within 300 s", message, StringComparison.Ordinal);
        Assert.Contains("export this project in EPLAN", message, StringComparison.Ordinal);
        Assert.DoesNotContain("pages", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "Line1.xml")]
    [InlineData("Line1 parts", "Line1 parts.xml")]
    [InlineData("Line1 parts.XML", "Line1 parts.XML")]
    [InlineData(".xml", "Line1.xml")]
    public async Task Parts_IsOneXmlFileInANewFolder_WithTheGivenNameOrTheProjects(string? fileName, string expected)
    {
        CallToolResult result = await Parts(fileName: fileName);

        JsonElement data = Structured(result);
        Assert.Equal(("Line1", expected, 1), (data.GetProperty("project").GetString(), Files(data)[0].Path, data.GetProperty("fileCount").GetInt32()));
        Assert.Equal(["project", "directory", "files", "fileCount", "totalBytes", "truncated", "hint"], data.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Exported the parts list to a 13 MB XML file in a new folder; see 'directory'.", Text(result, 0));
        Assert.Equal(expected, ((ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPartsRun).Args!).FileName);
        Assert.Equal([ExportRunner.Timeout], _bridge.Timeouts);
    }

    [Fact]
    public async Task Parts_AnInvalidFileName_IsRefusedBeforeAnythingIsCreated()
    {
        JsonElement error = Error(await Parts(fileName: "a/b"));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_bridge.Calls);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task Parts_NeedsNoFreeSpaceCheck_AndATimeoutSaysWhatToDo()
    {
        _free = 0;
        Structured(await Parts());

        _bridge.Handlers[OperationNames.ExportPartsRun] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.Timeout, Message = "m", Retryable = true });
        Assert.Contains("export this project in EPLAN", Error(await Parts()).GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Parts(string? project = null, string? fileName = null) =>
        ProjectExportTools.ExportPartsAsync(_runner, _progress, project, fileName, TestContext.Current.CancellationToken);

    private Task<CallToolResult> Pxf(string? project = null, bool masterData = false, string? fileName = null) =>
        ProjectExportTools.ExportPxfAsync(_runner, _progress, project, masterData, fileName, TestContext.Current.CancellationToken);

    private static (string Path, long Bytes)[] Files(JsonElement data) =>
        [.. data.GetProperty("files").EnumerateArray().Select(f => (f.GetProperty("path").GetString()!, f.GetProperty("bytes").GetInt64()))];

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
    }
}
