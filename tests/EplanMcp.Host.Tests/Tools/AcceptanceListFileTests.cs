using System.Text.Json;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class AcceptanceListFileTests
{
    [Fact]
    public async Task FictionalFile_NativePlacementAndWireBatches_PersistOnlyGenuineUnsupportedReasons()
    {
        string folder = Directory.CreateTempSubdirectory("eplanmcp-acceptance-").FullName;
        try
        {
            string fixture = Path.Combine(folder, "fixture.json");
            await File.WriteAllTextAsync(fixture, AcceptanceDrawingListTests.Fixture(), TestContext.Current.CancellationToken);
            JsonElement list = await AcceptanceListFile.LoadAsync(folder, fixture, null, TestContext.Current.CancellationToken);
            var fake = new AcceptanceRedrawDriverTests.DrawingTools { ConnectWires = true, PaginateDrawings = true, PaginateConnections = true };
            var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", list, "medium", _ => { });
            await driver.DrawAsync();
            await driver.ExportAsync();
            Assert.Equal(7, fake.ConnectionItems!.Value.GetArrayLength());
            Assert.Equal(41, driver.UndoSteps);
            string report = Path.Combine(folder, "report.json");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(driver.Report), TestContext.Current.CancellationToken);
            using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken));
            string[] skipped = saved.RootElement.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("item").GetString()!).ToArray();
            Assert.Equal(7, skipped.Length);
            Assert.All(skipped, id => Assert.EndsWith("/display_properties", id, StringComparison.Ordinal));
            Assert.Equal(2, fake.Requests.Count(r => r.Tool == "eplan_cable_get"));
            Assert.Equal(4, fake.Requests.Count(r => r.Tool == "eplan_connection_create"));
            JsonElement[] evidence = saved.RootElement.GetProperty("evidence").EnumerateArray().ToArray();
            JsonElement[] autoconnect = evidence.Where(e => e.TryGetProperty("wire", out _)).ToArray();
            Assert.Equal(13, autoconnect.Length);
            Assert.All(autoconnect, e => Assert.Equal("autoconnect", e.GetProperty("mode").GetString()));
            JsonElement[] writes = evidence.Where(e => e.TryGetProperty("tool", out JsonElement tool) && tool.GetString() == "eplan_connection_create").Select(e => e.GetProperty("result")).ToArray();
            Assert.Equal(7, writes[1].GetProperty("actualCores").GetArrayLength());
            Assert.Equal(10, writes[3].GetProperty("interruptionPoints").GetArrayLength());
            Assert.All(writes[3].GetProperty("interruptionPoints").EnumerateArray(), p => Assert.Single(p.GetProperty("counterparts").EnumerateArray()));
            Assert.False(driver.UndoCountIncomplete);
            int wireCall = fake.Requests.FindIndex(r => r.Tool == "eplan_connection_create");
            Assert.True(fake.Requests.FindIndex(r => r.Tool == "eplan_device_place" && r.Args.TryGetProperty("terminal_number", out _)) < wireCall);
            Assert.Equal("eplan_page_get", fake.Requests[wireCall - 1].Tool);
            Assert.Equal(2, fake.Calls.Count(c => c == "eplan_symbol_search"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task DefaultAndExternalFile_ReadWithoutCopyingPrivateInputIntoRepository()
    {
        string folder = Directory.CreateTempSubdirectory("eplanmcp-acceptance-").FullName;
        try
        {
            string repository = Directory.CreateDirectory(Path.Combine(folder, "repo")).FullName;
            string fixture = Path.Combine(repository, "fixture.json"), external = Path.Combine(folder, "local.json");
            await File.WriteAllTextAsync(fixture, AcceptanceDrawingListTests.Fixture(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(external, AcceptanceDrawingListTests.Fixture(), TestContext.Current.CancellationToken);
            JsonElement first = await AcceptanceListFile.LoadAsync(repository, fixture, null, TestContext.Current.CancellationToken);
            JsonElement second = await AcceptanceListFile.LoadAsync(repository, "not-used.json", external, TestContext.Current.CancellationToken);
            Assert.True(JsonElement.DeepEquals(first, second));
            Assert.Single(Directory.GetFiles(repository));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, fixture, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, @"\\server\share\private.json", TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => AcceptanceListFile.LoadAsync(repository, fixture, "relative.json", TestContext.Current.CancellationToken));
            await File.WriteAllTextAsync(external, new string(' ', AcceptanceDrawingList.MaxBytes + 1), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<JsonException>(() => AcceptanceListFile.LoadAsync(repository, fixture, external, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
