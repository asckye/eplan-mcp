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
/// The parts database export on a fake Bridge that writes into the folder the Host created, a real one in a temporary
/// data directory (docs/tools/eplan_part_export.md, sections 2 to 5 and 7). The steps it shares with eplan_export_pdf
/// are <see cref="EplanMcp.Host.Tests.Exports.ExportRunnerTests"/>'s.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MasterDataExportToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-database-export-tools-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();
    private readonly RecordedProgress _progress = new();
    private readonly ExportRunner _runner;

    public MasterDataExportToolTests()
    {
        _runner = new ExportRunner(_bridge, new ExportFolders(_locations, _time), _time, NullLogger<ExportRunner>.Instance);
        _bridge.Handlers[OperationNames.PartExportPlan] = args => new ExportPlanData { Database = @"$(MD_PARTS)\EPLAN_parts.alk", Extension = "." + ((ExportArgs)args!).Format };
        _bridge.Handlers[OperationNames.PartExportRun] = args =>
        {
            var run = (ExportArgs)args!;
            File.WriteAllBytes(Path.Combine(run.Directory!, run.FileName!), new byte[5 * 1024 * 1024]);
            return new ExportRunData { Database = @"$(MD_PARTS)\EPLAN_parts.alk", PartCount = run.Parts?.Count ?? 454, VariantCount = run.Parts?.Count ?? 464 };
        };
    }

    private string PartsFolder => Path.Combine(_locations.Exports, MasterDataExportTools.PartsFolder);

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task Parts_All_AreOneXmlFileInANewFolderOfTheirOwn()
    {
        CallToolResult result = await Parts();

        JsonElement data = Structured(result);
        string directory = data.GetProperty("directory").GetString()!;
        Assert.StartsWith(Path.Combine(PartsFolder, "20261021T083000Z-"), directory, StringComparison.Ordinal);
        Assert.Equal((@"$(MD_PARTS)\EPLAN_parts.alk", 454, 464), (data.GetProperty("database").GetString(), data.GetProperty("partCount").GetInt32(), data.GetProperty("variantCount").GetInt32()));
        Assert.Equal(("parts.xml", 1), (Files(data)[0], data.GetProperty("fileCount").GetInt32()));
        Assert.Equal(["database", "partCount", "variantCount", "directory", "files", "fileCount", "totalBytes", "truncated", "hint"], data.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Exported 454 parts from the parts database to a 5 MB XML file in a new folder; see 'directory'.", Text(result, 0));

        var plan = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.PartExportPlan).Args!;
        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.PartExportRun).Args!;
        Assert.Equal(("xml", (List<string>?)null, (string?)null), (plan.Format, plan.Parts, plan.Project));
        Assert.Equal((directory, "parts.xml", "xml", (List<string>?)null), (run.Directory, run.FileName, run.Format, run.Parts));
        Assert.Equal([ExportRunner.Timeout], _bridge.Timeouts);
    }

    [Theory]
    [InlineData("edz", "relays", "relays.edz", "EDZ")]
    [InlineData("EDZ", "relays.EDZ", "relays.EDZ", "EDZ")]
    [InlineData("xml", ".xml", "parts.xml", "XML")]
    public async Task Parts_Given_GoToTheBridgeAsGiven_InTheFormatAndUnderTheNameAsked(string format, string fileName, string expected, string kind)
    {
        CallToolResult result = await Parts(format, ["SIE.3RH2122-1HB40"], fileName);

        JsonElement data = Structured(result);
        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.PartExportRun).Args!;
        Assert.Equal((expected, format.ToLowerInvariant(), "SIE.3RH2122-1HB40"), (run.FileName, run.Format, run.Parts!.Single()));
        Assert.Equal((expected, 1), (Files(data)[0], data.GetProperty("partCount").GetInt32()));
        Assert.Equal($"Exported 1 part from the parts database to a 5 MB {kind} file in a new folder; see 'directory'.", Text(result, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Parts_EveryArgumentProblem_IsReportedAtOnce_BeforeAnythingIsCreated(int count)
    {
        JsonElement error = Error(await Parts("csv", [.. Enumerable.Range(0, count).Select(i => "P." + i)], "a/b"));

        string violations = error.GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.All(["'format' must be xml or edz.", "'parts' must hold 1 to 500", "'file_name'"], v => Assert.Contains(v, violations, StringComparison.Ordinal));
        Assert.Empty(_bridge.Calls);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task Parts_APartTheDatabaseDoesNotHave_IsTheBridgesErrorWithTheMissing_AndNoFolder()
    {
        _bridge.Handlers[OperationNames.PartExportPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.PartNotFound,
            Message = "The parts database has no part with some of these part numbers, so nothing was exported; metadata.missing lists them.",
            Metadata = new Dictionary<string, string> { ["missing"] = "SIE.NONE", ["missingCount"] = "1" },
        });

        JsonElement error = Error(await Parts(parts: ["SIE.3RH2122-1HB40", "SIE.NONE"]));

        Assert.Equal((ErrorReason.PartNotFound, "SIE.NONE", "1"), (error.GetProperty("reason").GetString(), error.GetProperty("metadata").GetProperty("missing").GetString(), error.GetProperty("metadata").GetProperty("missingCount").GetString()));
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.PartExportRun);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task Parts_AFailedRun_KeepsNoFolder()
    {
        _bridge.Handlers[OperationNames.PartExportRun] = args =>
        {
            File.WriteAllText(Path.Combine(((ExportArgs)args!).Directory!, "parts.xml"), "<partsmanagement");
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "EPLAN could not export its parts database, so no files were kept." });
        };

        Assert.Equal(ErrorReason.EplanOperationFailed, Error(await Parts()).GetProperty("reason").GetString());
        Assert.Empty(Directory.GetFileSystemEntries(PartsFolder));
    }

    [Fact]
    public async Task Parts_AnythingButTheOneFile_FailsTheExport_AndKeepsNoFolder()
    {
        Func<object?, object> export = _bridge.Handlers[OperationNames.PartExportRun];
        _bridge.Handlers[OperationNames.PartExportRun] = args =>
        {
            object data = export(args);
            File.WriteAllText(Path.Combine(((ExportArgs)args!).Directory!, "parts.log"), "x");
            return data;
        };

        JsonElement error = Error(await Parts("edz", fileName: "all"));

        Assert.Equal(ErrorReason.EplanOperationFailed, error.GetProperty("reason").GetString());
        Assert.Contains("other files than one .edz file", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(PartsFolder));
    }

    [Fact]
    public async Task Parts_ACancel_DeletesTheFolder()
    {
        _bridge.Handlers[OperationNames.PartExportRun] = args =>
        {
            File.WriteAllText(Path.Combine(((ExportArgs)args!).Directory!, "parts.edz"), "half");
            throw new OperationCanceledException();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() => Parts("edz"));

        Assert.Empty(Directory.GetFileSystemEntries(PartsFolder));
    }

    [Fact]
    public async Task Parts_ATimeout_SaysToExportFewerPartsOrAsXml()
    {
        _bridge.Handlers[OperationNames.PartExportRun] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.Timeout, Message = "m", Retryable = true });

        JsonElement error = Error(await Parts("edz"));

        Assert.Equal(ErrorReason.Timeout, error.GetProperty("reason").GetString());
        Assert.Contains("did not finish within 300 s", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("fewer parts per call", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Parts(string format = "xml", string[]? parts = null, string? fileName = null) =>
        MasterDataExportTools.ExportPartsAsync(_runner, _progress, format, parts, fileName, TestContext.Current.CancellationToken);

    private static string[] Files(JsonElement data) => [.. data.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()!)];

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
