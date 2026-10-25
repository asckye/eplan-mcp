using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>Public property tool through LiveEplan's real stdio Host, on the unchanged read-only sample.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PropertyGetToolTests(LiveEplan eplan)
{
    private static readonly string[] _connectionKinds = ["connection"];
    private static readonly string[] _objectsOnly = ["objects"];

    [Fact]
    public async Task PropertyGet_MixedTargetsFormalNamesIdsLanguageAndRefusal_ThroughStdio()
    {
        await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        SnapshotObject device = LivePropertyObjects.Device(snapshot);
        SnapshotValue text = Assert.Single(device.Values, v => v.Id == 20011);
        SnapshotText translation = text.Texts!.First(t => t.Language != "??_??");
        SnapshotObject connection = LivePropertyObjects.Connection(snapshot);
        JsonElement context = await eplan.CallAsync("eplan_context_get", new()
        {
            ["project"] = eplan.ProjectPath,
            ["scope"] = new { pages = new[] { connection.Page }, kinds = _connectionKinds },
            ["include"] = _objectsOnly,
            ["max_tokens"] = 20000,
        });
        JsonElement connectionLocator = context.GetProperty("objects")[0].GetProperty("locator");
        Assert.Equal("connection", connectionLocator.GetProperty("kind").GetString());
        Assert.Equal(eplan.ProjectPath, connectionLocator.GetProperty("project").GetString(), ignoreCase: true);
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        JsonElement result = await eplan.CallAsync("eplan_property_get", new()
        {
            ["project"] = eplan.ProjectPath,
            ["queries"] = new object[]
            {
                new { target = new { project_object = true }, properties = new[] { new { property_name = "PROJ_DRAWINGNUMBER" } } },
                new { target = new { page = LiveProperties.PowerSupplyPage }, properties = new[] { new { property_id = 11011 }, new { property_id = 3010 } } },
                new { target = new { device_tag = device.Name }, properties = new[] { new { property_name = "FUNC_TEXT" } } },
                new { target = new { locator = connectionLocator }, properties = new[] { new { property_id = 31002 } } },
            },
            ["language"] = translation.Language,
            ["response_format"] = "detailed",
            ["limit"] = 200,
        });
        Assert.Equal(LiveEplan.ProjectName, result.GetProperty("project").GetString());
        Assert.Equal(5, result.GetProperty("total").GetInt32());
        Assert.False(result.GetProperty("truncated").GetBoolean());
        JsonElement[] rows = [.. result.GetProperty("items").EnumerateArray()];
        Assert.Equal(5, rows.Length);
        JsonElement function = Assert.Single(rows, r => r.GetProperty("query").GetInt32() == 2);
        Assert.Equal(("value", translation.Language, translation.Text, false), (function.GetProperty("status").GetString(),
            function.GetProperty("language").GetString(), function.GetProperty("value").GetString(), function.GetProperty("fallback").GetBoolean()));
        JsonElement refused = Assert.Single(rows, r => r.GetProperty("property_id").GetInt32() == 3010);
        Assert.Equal(("read_refused", PropertyCatalog.ReadRefusal("Page", 3010)), (refused.GetProperty("status").GetString(), refused.GetProperty("reason").GetString()));
        foreach (string field in new[] { "value", "display", "texts", "language" })
        {
            Assert.Equal(JsonValueKind.Null, refused.GetProperty(field).ValueKind);
        }
        JsonElement wire = Assert.Single(rows, r => r.GetProperty("query").GetInt32() == 3);
        Assert.Equal(31002, wire.GetProperty("property_id").GetInt32());
        Assert.Equal("value", wire.GetProperty("status").GetString());
        Assert.Equal(connectionLocator.GetProperty("oid").GetInt64(), wire.GetProperty("target").GetProperty("locator").GetProperty("oid").GetInt64());
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(eplan.ProjectPath).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PropertyGet_AmbiguousFullDeviceTag_ReturnsCandidateLocatorsAndAcceptsAChoice_ThroughStdio()
    {
        await using var bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        string tag = snapshot.Objects("function").Where(o => o.Main == true && o.Name is not null)
            .GroupBy(o => o.Name, StringComparer.Ordinal)
            .First(g => g.Select(o => (o.Type, o.Oid)).Distinct().Count() > 1).Key!;
        JsonElement error = await eplan.ErrorAsync("eplan_property_get", new()
        {
            ["project"] = eplan.ProjectPath,
            ["queries"] = new[] { new { target = new { device_tag = tag }, properties = new[] { new { property_id = 20011 } } } },
        });
        Assert.Equal(ErrorReason.AmbiguousName, error.GetProperty("reason").GetString());
        Assert.Equal("/queries/0/target", error.GetProperty("metadata").GetProperty("pointer").GetString());
        JsonElement candidates = JsonSerializer.Deserialize<JsonElement>(error.GetProperty("metadata").GetProperty("candidates").GetString()!);
        Assert.InRange(candidates.GetArrayLength(), 2, 20);
        Assert.All(candidates.EnumerateArray(), c =>
        {
            Assert.Equal("function", c.GetProperty("kind").GetString());
            Assert.Equal(tag, c.GetProperty("name").GetString());
            Assert.True(c.GetProperty("oid").GetInt64() > 0);
        });
        JsonElement resolved = await eplan.CallAsync("eplan_property_get", new()
        {
            ["project"] = eplan.ProjectPath,
            ["queries"] = new[] { new { target = new { locator = candidates[0] }, properties = new[] { new { property_id = 20011 } } } },
            ["language"] = "zh_CN",
        });
        Assert.Single(resolved.GetProperty("items").EnumerateArray());
        Assert.Equal(1, resolved.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task PropertyGet_CursorPagesReassembleTheExactSelection_ThroughStdio()
    {
        var args = new Dictionary<string, object?>
        {
            ["project"] = eplan.ProjectPath,
            ["queries"] = new object[]
            {
                new { target = new { project_object = true }, properties = new[] { new { property_id = 10901 }, new { property_id = 10013 } } },
                new { target = new { page = LiveProperties.PowerSupplyPage }, properties = new[] { new { property_name = "PAGE_NOMINATIOMN" } } },
            },
            ["language"] = "zh_CN",
            ["response_format"] = "detailed",
            ["limit"] = 200,
        };
        JsonElement whole = await eplan.CallAsync("eplan_property_get", args);
        Assert.False(whole.GetProperty("truncated").GetBoolean());
        Assert.True(whole.GetProperty("total").GetInt32() >= 3);
        args["limit"] = 1;
        var rows = new List<JsonElement>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            JsonElement page = await eplan.CallAsync("eplan_property_get", args);
            Assert.Single(page.GetProperty("items").EnumerateArray());
            rows.AddRange(page.GetProperty("items").EnumerateArray());
            string? cursor = page.GetProperty("cursor").GetString();
            args["cursor"] = cursor;
            if (cursor is not null)
            {
                Assert.True(cursors.Add(cursor));
                Assert.True(page.GetProperty("truncated").GetBoolean());
            }
            Assert.InRange(rows.Count, 1, whole.GetProperty("total").GetInt32());
        }
        while (args["cursor"] is not null);
        Assert.True(JsonElement.DeepEquals(whole.GetProperty("items"), JsonSerializer.SerializeToElement(rows)));
    }
}
