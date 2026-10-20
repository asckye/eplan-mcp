using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_export_image on a fake Bridge that writes one image per page into the folder the Host created
/// (docs/tools/eplan_export_image.md, sections 2 to 4): the page of each file, and the images <c>inline</c> attaches.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ImageExportToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-export-image-").FullName);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
    private readonly FakeBridgeClient _bridge = new();
    private readonly ExportRunner _runner;

    public ImageExportToolTests()
    {
        _runner = new ExportRunner(_bridge, new ExportFolders(_locations, _time), _time, NullLogger<ExportRunner>.Instance);
        _bridge.Handlers[OperationNames.ExportImagePlan] = args => new ExportPlanData { Project = "Line1", PageCount = ((ExportArgs)args!).Pages!.Count };
        _bridge.Handlers[OperationNames.ExportImageRun] = args =>
        {
            var run = (ExportArgs)args!;
            var files = new List<ExportedPage>();
            foreach ((string page, int i) in run.Pages!.Select((p, i) => (p, i)))
            {
                string path = page.Replace('/', '_') + "." + run.Format;
                File.WriteAllBytes(Path.Combine(run.Directory!, path), [.. Enumerable.Repeat((byte)(i + 1), 100 + i)]);
                files.Add(new ExportedPage { Page = page, Path = path });
            }

            return new ExportRunData { Project = "Line1", PageCount = files.Count, Files = files };
        };
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task Inline_AttachesEachImageAfterTheJson()
    {
        CallToolResult result = await Image(["=GAA+A1&EFS1/60"], inline: true);

        JsonElement data = Structured(result);
        Assert.Equal([("=GAA+A1&EFS1/60", "=GAA+A1&EFS1_60.png", 100L)], Files(data));
        Assert.Equal((1, 1, 100L), (data.GetProperty("inline").GetInt32(), data.GetProperty("fileCount").GetInt32(), data.GetProperty("totalBytes").GetInt64()));
        Assert.Equal("Exported 1 page as a PNG image in a new folder; the image is included below.", ((TextContentBlock)result.Content[0]).Text);
        ImageContentBlock image = Assert.IsType<ImageContentBlock>(result.Content[2]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(File.ReadAllBytes(Path.Combine(data.GetProperty("directory").GetString()!, "=GAA+A1&EFS1_60.png")), image.DecodedData.ToArray());
        Assert.Equal(3, result.Content.Count);
    }

    [Fact]
    public async Task WithoutInline_TheFilesAreListedInTheOrderOfThePages()
    {
        CallToolResult result = await Image(["=GAA+A1&EFS1/2", "=GAA+A1&EFS1/1"], format: "jpg", width: 2400);

        JsonElement data = Structured(result);
        Assert.Equal([("=GAA+A1&EFS1/2", "=GAA+A1&EFS1_2.jpg", 100L), ("=GAA+A1&EFS1/1", "=GAA+A1&EFS1_1.jpg", 101L)], Files(data));
        Assert.Equal(0, data.GetProperty("inline").GetInt32());
        Assert.Equal("Exported 2 pages as JPG images in a new folder; see 'directory'.", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(2, result.Content.Count);
        var run = (ExportArgs)_bridge.Calls.Single(c => c.Operation == OperationNames.ExportImageRun).Args!;
        Assert.Equal(("jpg", (int?)2400), (run.Format, run.WidthPx));
    }

    [Fact]
    public async Task SeveralInlineImages_KeepTheOrderOfFiles()
    {
        CallToolResult result = await Image(["=GAA+A1&EFS1/3", "=GAA+A1&EFS1/1"], format: "jpg", inline: true);

        Assert.Equal("Exported 2 pages as JPG images in a new folder; the images are included below.", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal([(byte)1, (byte)2], result.Content.Skip(2).Cast<ImageContentBlock>().Select(i => i.DecodedData.Span[0]));
        Assert.All(result.Content.Skip(2).Cast<ImageContentBlock>(), i => Assert.Equal("image/jpeg", i.MimeType));
    }

    [Fact]
    public async Task ArgumentProblems_AreAllReportedBeforeAnythingIsCreated()
    {
        CallToolResult result = await Image([.. Enumerable.Range(1, 5).Select(i => "=A/" + i)], width: 100, inline: true);

        string violations = Error(result).GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Contains("'width_px' must be between 200 and 4000", violations, StringComparison.Ordinal);
        Assert.Contains("'inline' takes at most 4 pages", violations, StringComparison.Ordinal);
        Assert.Contains("'pages' must hold 1 to 20", Error(await Image([])).GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("'pages' must hold 1 to 20", Error(await Image([.. Enumerable.Range(1, 21).Select(i => "=A/" + i)])).GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_bridge.Calls);
        Assert.False(Directory.Exists(_locations.Exports));
    }

    [Fact]
    public async Task AnImageNotInTheFolder_IsNotAttached_AndTheHintNamesItsPage()
    {
        _bridge.Handlers[OperationNames.ExportImageRun] = args =>
        {
            File.WriteAllBytes(Path.Combine(((ExportArgs)args!).Directory!, "other.png"), [1]);
            return new ExportRunData { Project = "Line1", PageCount = 1, Files = [new ExportedPage { Page = "=GAA+A1&EFS1/60", Path = @"..\outside.png" }] };
        };

        JsonElement data = Structured(await Image(["=GAA+A1&EFS1/60"], inline: true));

        Assert.Equal(0, data.GetProperty("inline").GetInt32());
        Assert.Contains("=GAA+A1&EFS1/60", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Image(string[] pages, string format = "png", int width = 1600, bool inline = false) =>
        ImageExportTools.ExportImageAsync(_runner, new RecordedProgress(), pages, project: null, format, width, inline, TestContext.Current.CancellationToken);

    private static (string Page, string Path, long Bytes)[] Files(JsonElement data) =>
        [.. data.GetProperty("files").EnumerateArray().Select(f => (f.GetProperty("page").GetString()!, f.GetProperty("path").GetString()!, f.GetProperty("bytes").GetInt64()))];

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }
}
