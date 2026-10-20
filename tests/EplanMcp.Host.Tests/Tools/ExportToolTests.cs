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
/// eplan_export_pdf and eplan_export_dxf on a fake Bridge that writes into the folder the Host created, a real one in a
/// temporary data directory (docs/tools/eplan_export_pdf.md, eplan_export_dxf.md, sections 2 to 4). The steps they share,
/// failures and cancels included, are <see cref="EplanMcp.Host.Tests.Exports.ExportRunnerTests"/>'s.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ExportToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-export-tools-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();
    private readonly RecordedProgress _progress = new();
    private readonly ExportRunner _runner;

    public ExportToolTests()
    {
        _runner = new ExportRunner(_bridge, new ExportFolders(_locations, _time), _time, NullLogger<ExportRunner>.Instance);
        _bridge.Handlers[OperationNames.ExportPdfPlan] = _ => new ExportPlanData { Project = "Line1", PageCount = 3, Scheme = "EPLAN_default_value" };
        _bridge.Handlers[OperationNames.ExportDxfPlan] = _ => new ExportPlanData { Project = "Line1", PageCount = 2, Scheme = "Standard Settings" };
        _bridge.Handlers[OperationNames.ExportPdfRun] = args =>
        {
            Write((ExportArgs)args!, ((ExportArgs)args!).FileName!, 208_896);
            return new ExportRunData { Project = "Line1", PageCount = 3, Scheme = "EPLAN_default_value" };
        };
        _bridge.Handlers[OperationNames.ExportDxfRun] = args => Write((ExportArgs)args!, @"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 151_552, @"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 98_304);
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task Pdf_IsOneFileInANewFolder_NamedAfterTheProject()
    {
        CallToolResult result = await Pdf(pages: ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/3"]);

        JsonElement data = Structured(result);
        string directory = data.GetProperty("directory").GetString()!;
        Assert.StartsWith(Path.Combine(_locations.Exports, "Line1", "20261021T083000Z-"), directory, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory, "Line1.pdf")));
        Assert.Equal(("Line1", 3, "EPLAN_default_value"), (data.GetProperty("project").GetString(), data.GetProperty("pageCount").GetInt32(), data.GetProperty("scheme").GetString()));
        Assert.Equal(("Line1.pdf", 208_896L, 1, 208_896L, false), (Files(data)[0].Path, Files(data)[0].Bytes, data.GetProperty("fileCount").GetInt32(), data.GetProperty("totalBytes").GetInt64(), data.GetProperty("truncated").GetBoolean()));
        Assert.Equal("Exported 3 pages to 1 PDF file (204 KB) in a new folder; see 'directory'.", Text(result, 0));

        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPdfRun).Args!;
        Assert.Equal((directory, "Line1.pdf", "color"), (run.Directory, run.FileName, run.Color));
        Assert.Equal([ExportRunner.Timeout], _bridge.Timeouts);
    }

    [Fact]
    public async Task Pdf_TakesTheGivenFileName_AndAddsTheExtension()
    {
        await Pdf(fileName: "Line1 power", color: "grey");

        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportPdfRun).Args!;
        Assert.Equal(("Line1 power.pdf", "grey", (List<string>?)null), (run.FileName, run.Color, run.Pages));
    }

    [Fact]
    public async Task Pdf_ArgumentProblems_AreAllReportedBeforeAnythingIsCreated()
    {
        CallToolResult result = await Pdf(pages: [], fileName: @"..\x");

        string violations = Error(result).GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Contains("'pages' must hold 1 to 50", violations, StringComparison.Ordinal);
        Assert.Contains("'file_name' must be a file name only", violations, StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task APlanError_CreatesNoFolder()
    {
        _bridge.Handlers[OperationNames.ExportPdfPlan] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PageNotFound, Message = "No page has these names." });

        Assert.Equal(ErrorReason.PageNotFound, Error(await Pdf()).GetProperty("reason").GetString());
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task ARunError_IsTheToolsError_AndKeepsNoFolder()
    {
        _bridge.Handlers[OperationNames.ExportDxfRun] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "EPLAN could not export these pages." });

        Assert.Equal(ErrorReason.EplanOperationFailed, Error(await Dxf()).GetProperty("reason").GetString());
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_locations.Exports, "Line1")));
    }

    [Fact]
    public async Task Dxf_ListsTheFilesInTheSchemesSubfolders()
    {
        CallToolResult result = await Dxf(pages: ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], scheme: "Standard Settings");

        JsonElement data = Structured(result);
        Assert.Equal([(@"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 151_552L), (@"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 98_304L)], Files(data));
        Assert.Equal("Exported 2 pages to 2 DXF files (244 KB) in a new folder; see 'directory'.", Text(result, 0));
        Assert.Equal("Standard Settings", ((ExportArgs)_bridge.Calls[0].Args!).Scheme);
    }

    [Fact]
    public async Task Dxf_SaysDwgWhenEveryFileIsOne()
    {
        _bridge.Handlers[OperationNames.ExportDxfRun] = args => Write((ExportArgs)args!, "a.dwg", 1024, "b.DWG", 1024);

        Assert.Equal("Exported 2 pages to 2 DWG files (2 KB) in a new folder; see 'directory'.", Text(await Dxf(), 0));
    }

    [Fact]
    public async Task MoreThan50Files_AreCountedButNotAllListed_AndAWarningIsAHint()
    {
        _bridge.Handlers[OperationNames.ExportDxfRun] = args =>
        {
            foreach (int i in Enumerable.Range(100, 60))
            {
                Write((ExportArgs)args!, $"p{i}.dxf", 1);
            }

            return new ExportRunData { Project = "Line1", PageCount = 60, Scheme = "Standard Settings", Warning = "The scheme‮ could not be set back." };
        };

        JsonElement data = Structured(await Dxf());

        Assert.Equal((50, 60, true), (data.GetProperty("files").GetArrayLength(), data.GetProperty("fileCount").GetInt32(), data.GetProperty("truncated").GetBoolean()));
        Assert.StartsWith("The scheme could not be set back. Not every file is listed", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Pdf(string[]? pages = null, string? fileName = null, string color = "color") =>
        ExportTools.ExportPdfAsync(_runner, _progress, project: null, pages, scheme: null, color, fileName, TestContext.Current.CancellationToken);

    private Task<CallToolResult> Dxf(string[]? pages = null, string? scheme = null) =>
        ExportTools.ExportDxfAsync(_runner, _progress, project: null, pages, scheme, TestContext.Current.CancellationToken);

    /// <summary>Writes files of the given sizes below the run's folder, as EPLAN would, and answers like the Bridge.</summary>
    private static ExportRunData Write(ExportArgs args, params object[] files)
    {
        for (int i = 0; i < files.Length; i += 2)
        {
            string path = Path.Combine(args.Directory!, (string)files[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[(int)files[i + 1]]);
        }

        return new ExportRunData { Project = "Line1", PageCount = files.Length / 2, Scheme = "Standard Settings" };
    }

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
