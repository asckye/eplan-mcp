using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementDeleteCableTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    [Fact]
    public async Task CableDefinitionLine_IsDeleted_WithItsDefinitionPoints_UndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        var (created, creation) = await CablePage.CreateAsync(host, bridge, session.Project, page);
        Assert.True(created, creation.GetRawText());
        PageDrawingGeometry before = await CablePage.ReadAsync(bridge, session.Project, page);
        DrawingPlacement cable = Assert.Single(before.Placements, row => row.Kind == "Cable");
        var points = before.Placements.Where(row => row.Kind == "ConnectionDefinitionPoint")
            .Select(row => (row.Locator.Type, row.Locator.Oid)).Order().ToArray();
        Assert.NotEmpty(points);
        JsonElement deleted = await DeleteAsync(session.Project, page, cable);
        Assert.Equal(points, deleted.GetProperty("alsoRemoved").EnumerateArray().Select(row =>
        {
            Assert.Equal("ConnectionDefinitionPoint", row.GetProperty("kind").GetString());
            return (row.GetProperty("type").GetInt32(), row.GetProperty("oid").GetInt64());
        }).Order());
        PageDrawingGeometry after = await CablePage.ReadAsync(bridge, session.Project, page);
        Assert.DoesNotContain(after.Placements, row => row.Kind is "Cable" or "ConnectionDefinitionPoint");
        await session.UndoAsync();
        PageDrawingGeometry restored = await CablePage.ReadAsync(bridge, session.Project, page);
        Assert.Equal(cable.Properties["name"], Assert.Single(restored.Placements, row => row.Kind == "Cable").Properties["name"]);
    }

    [Fact]
    public async Task DeviceWiredToAPlcPoint_IsDeleted_Committed_ListsWhatElseChanged()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string suffix = page.Split('/')[^1];
        string project = session.Project;
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        double grid = (await CablePage.ReadAsync(bridge, project, page)).GridSize;
        JsonElement card = await host.CallAsync("eplan_device_place", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["tag"] = "-KF" + suffix,
            ["x"] = 12 * grid,
            ["y"] = 16 * grid,
            ["library"] = "SPECIAL",
            ["symbol"] = "PLCC",
            ["symbol_variant"] = 0,
            ["dry_run"] = false
        });
        string plcTag = card.GetProperty("device").GetProperty("tag").GetString()!;
        JsonElement plc = await FindSymbolAsync(project, s => s.TryGetProperty("plcInputDefinition", out var definition)
            && definition.ValueKind == JsonValueKind.String && s.GetProperty("connectionPoints").GetArrayLength() == 1 && Pin(s, "Down") is not null);
        JsonElement contact = await FindSymbolAsync(project, s => s.TryGetProperty("functionCategory", out var category)
            && category.GetString() == "NoContact" && Pin(s, "Up") is not null);

        Dictionary<string, object?> Place(JsonElement symbol, string direction, string tag, int pinY)
        {
            JsonElement offset = Pin(symbol, direction)!.Value.GetProperty("location");
            return new()
            {
                ["project"] = project,
                ["page"] = page,
                ["tag"] = tag,
                ["dry_run"] = false,
                ["library"] = symbol.GetProperty("library"),
                ["symbol"] = symbol.GetProperty("name"),
                ["symbol_variant"] = symbol.GetProperty("variant"),
                ["x"] = 32 * grid - offset[0].GetDouble(),
                ["y"] = pinY * grid - offset[1].GetDouble()
            };
        }

        var point = Place(plc, "Down", plcTag, 42);
        point["plc_connection_point"] = "X10:2";
        point["plc_address"] = "I" + suffix + ".0";
        await host.CallAsync("eplan_device_place", point);
        JsonElement device = await host.CallAsync("eplan_device_place", Place(contact, "Up", "-ST" + suffix, 30));
        string tag = device.GetProperty("device").GetProperty("tag").GetString()!;
        await host.CallAsync("eplan_connection_generate", new() { ["project"] = project, ["pages"] = new[] { page }, ["dry_run"] = false });
        JsonElement connections = await host.CallAsync("eplan_connection_list", new() { ["project"] = project, ["page"] = page });
        Assert.Contains(connections.GetProperty("connections").EnumerateArray(), c =>
        {
            string? from = Endpoint(c, "from");
            string? to = Endpoint(c, "to");
            return from == tag && IsPlc(to) || to == tag && IsPlc(from);
        });
        bool IsPlc(string? name) => name == plcTag || name?.StartsWith(plcTag + ":", StringComparison.Ordinal) == true;
        PageDrawingGeometry before = await CablePage.ReadAsync(bridge, project, page);
        DrawingPlacement target = Assert.Single(before.Placements, r => r.Parent is null && r.Properties.GetValueOrDefault("name") == tag);
        JsonElement result = await DeleteAsync(project, page, target);
        TestContext.Current.TestOutputHelper!.WriteLine("changed: " + (result.TryGetProperty("changed", out var changed) ? changed.GetRawText() : "(absent)"));
        Assert.DoesNotContain((await CablePage.ReadAsync(bridge, project, page)).Placements, r => r.Properties.GetValueOrDefault("name") == tag);
    }

    private static string? Endpoint(JsonElement connection, string end) => connection.GetProperty(end) is { ValueKind: JsonValueKind.Object } point
        ? point.GetProperty("device").GetString() : null;

    private static JsonElement? Pin(JsonElement symbol, string direction) => symbol.GetProperty("connectionPoints").EnumerateArray()
        .Where(p => p.GetProperty("direction").GetString() == direction).Select(p => (JsonElement?)p).FirstOrDefault();

    private async Task<JsonElement> FindSymbolAsync(string project, Func<JsonElement, bool> matches)
    {
        string? cursor = null;
        do
        {
            JsonElement found = await host.CallAsync("eplan_symbol_search", new() { ["project"] = project, ["kind"] = "function", ["limit"] = 200, ["cursor"] = cursor });
            foreach (JsonElement symbol in found.GetProperty("symbols").EnumerateArray())
            {
                if (matches(symbol))
                {
                    return symbol;
                }
            }
            cursor = found.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        throw new InvalidOperationException("No symbol with the required function and pin direction was found.");
    }

    private async Task<JsonElement> DeleteAsync(string project, string page, DrawingPlacement target)
    {
        string impact = target.Kind == "Cable"
            ? "Removes the cable definition line; EPLAN also removes the connection definition points it placed on the wires."
            : "Removing the last placed function also deletes its device and part references.";
        host.Approvals.Approve("eplan_placement_delete", project, [$"delete function {target.Properties["name"]} ({target.Kind}) ({impact})"]);
        var (_, result) = await host.CallOrErrorAsync("eplan_placement_delete", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["dry_run"] = false,
            ["items"] = JsonSerializer.SerializeToElement(new[] { target.Locator }, JsonSerializerOptions.Web)
        });
        TestContext.Current.TestOutputHelper!.WriteLine("eplan_placement_delete: " + result.GetRawText());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        return result;
    }
}

internal static class CablePage
{
    internal static Task<PageDrawingGeometry> ReadAsync(BridgeClient bridge, string project, string page) =>
        bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet, new() { Project = project, Page = page }, TestContext.Current.CancellationToken);

    internal static async Task<(bool Succeeded, JsonElement Result)> CreateAsync(DestructiveHost host, BridgeClient bridge, string project, string page)
    {
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        await PlanStepLiveSession.ActionAsync($"EplApiModuleAction /register:\"{helper}\" /Quiet");
        try
        {
            await PlanStepLiveSession.ActionAsync($"EplanMcpLargeConnectionSetup /PAGE:\"{page}\" /DIRECTIONS:Cable");
            await PlanStepLiveSession.ActionAsync($"EplanMcpLargeConnectionSetup /PAGE:\"{page}\" /DIRECTIONS:CableRead");
            using JsonDocument inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(LiveEplan.DataRoot(), "logs", "large-cable-inventory.json"), TestContext.Current.CancellationToken));
            if (inventory.RootElement.GetProperty("page").GetString() != page)
            {
                throw new InvalidOperationException("The cable fixture inventory names another page.");
            }
            string[] colours = ["BK", "BU", "GNYE"];
            var cores = inventory.RootElement.GetProperty("wires").EnumerateArray().Select((wire, i) => new
            {
                connection = new { locator = wire.GetProperty("Locator").Deserialize<DrawingLocator>() },
                label = (i + 1).ToString(CultureInfo.InvariantCulture),
                colour = colours[i]
            }).ToArray();
            double grid = (await ReadAsync(bridge, project, page)).GridSize;
            return await host.CallOrErrorAsync("eplan_connection_create", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["dry_run"] = false,
                ["response_format"] = "detailed",
                ["items"] = JsonSerializer.SerializeToElement(new[] { new
                {
                    kind = "cable_definition", points = new double[][] { [16 * grid, 35 * grid], [55 * grid, 35 * grid] },
                    designation = "-W9", type_text = "ÖLFLEX CLASSIC 100 300/500V", core_count = 3, cross_section_mm2 = 0.5, cores
                } }, JsonSerializerOptions.Web)
            });
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }
}
