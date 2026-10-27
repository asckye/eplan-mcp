using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PageDrawingCursorTests
{
    [Fact]
    public async Task Cursor_AtTenMinutes_ExpiresWithoutWaiting()
    {
        var clock = new ManualTime(DateTimeOffset.UnixEpoch);
        FakeBridgeClient bridge = PageDrawingToolTests.Bridge(2, clock: clock);
        string cursor = PageDrawingToolTests.Data(await PageDrawingToolTests.Read(bridge, 1)).GetProperty("drawings").GetProperty("nextCursor").GetString()!;
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True((await PageDrawingToolTests.Read(bridge, 1, cursor)).IsError);
    }

    [Fact]
    public async Task PersistedCursor_RejectsChangedPageGeometryProjectAndBridgeInstance()
    {
        FakeBridgeClient bridge = PageDrawingToolTests.Bridge(2);
        var geometry = (PageDrawingGeometry)bridge.Handlers[OperationNames.PageDrawingGet](null);
        string cursor = PageDrawingToolTests.Data(await PageDrawingToolTests.Read(bridge, 1)).GetProperty("drawings").GetProperty("nextCursor").GetString()!;
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, cursor, TestContext.Current.CancellationToken);
            string saved = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            PageDrawingToolTests.Data(await PageDrawingToolTests.Read(bridge, 1, saved));
            DrawingLocator original = geometry.Placements[0].Locator;
            geometry.Placements[0].Locator = new() { Project = original.Project, Page = original.Page, Type = original.Type, Oid = original.Oid, Digest = new string('b', 64) };
            await Stale(saved);
            geometry.Placements[0].Locator = original;
            geometry.GridSize = 8;
            await Stale(saved);
            geometry.GridSize = 4;
            geometry.Project = "X:/Other";
            await Stale(saved);
            geometry.Project = "X:/Line1";
            geometry.Page = "Other";
            await Stale(saved);
            geometry.Page = "P";
            DrawingPlacement removed = geometry.Placements[1];
            geometry.Placements.RemoveAt(1);
            await Stale(saved);
            geometry.Placements.Add(removed);
            bridge.Status = new([], new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 42 } }, null);
            await Stale(saved);
            bridge.Status = new([], null, null);
            Assert.True((await PageDrawingToolTests.Read(bridge, 1, saved, "detailed")).IsError);
            Assert.True((await PageDrawingToolTests.Read(PageDrawingToolTests.Bridge(2), 1, saved)).IsError);
            Assert.True((await PageDrawingToolTests.Read(bridge, 1, saved + "!")).IsError);
            Assert.Empty(bridge.Writes);

            async Task Stale(string given)
            {
                CallToolResult error = await PageDrawingToolTests.Read(bridge, 1, given);
                Assert.True(error.IsError);
                using JsonDocument json = JsonDocument.Parse(((TextContentBlock)error.Content[0]).Text);
                Assert.Equal(ErrorReason.InvalidArgument, json.RootElement.GetProperty("error").GetProperty("reason").GetString());
            }
        }
        finally
        {
            File.Delete(file);
        }
    }
}
