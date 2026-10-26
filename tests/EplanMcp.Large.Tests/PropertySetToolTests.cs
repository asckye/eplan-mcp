using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PropertySetToolTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task B1_ThroughHost_WriteBothPropertiesReadBackAndRestoreOriginalValues()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        Assert.StartsWith(WriteHost.ScratchFolder + Path.DirectorySeparatorChar, host.Writable, StringComparison.OrdinalIgnoreCase);
        await using var reader = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(reader, eplan.ProcessId);
        await using PropertyWriteBridge planner = await PropertyWriteBridge.StartAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(reader, new() { Project = host.Writable });
        PropertySetArgs baseline = await PropertySetWriteTests.CandidatesAsync(planner, reader, host.Writable, snapshot);
        List<PropertySetChange> restore = PropertySetWriteTests.RestoreChanges(baseline);
        Assert.All(restore, c => Assert.InRange(c.Value.Text!.Length, 0, 500));
        JsonElement before = await ReadAsync(baseline.Changes);
        int recordsBefore = host.AuditRecords().Count;
        JsonElement preview = await host.CallAsync("eplan_property_set", Args(baseline.Changes, dryRun: true));
        Assert.Equal(("not_started", 2), (preview.GetProperty("execution").GetString(), preview.GetProperty("changed_count").GetInt32()));
        Assert.True(JsonElement.DeepEquals(before, await ReadAsync(baseline.Changes)));
        Assert.Equal(recordsBefore, host.AuditRecords().Count);
        try
        {
            JsonElement committed = await host.CallAsync("eplan_property_set", Args(baseline.Changes));
            Assert.Equal(("committed", 2, "AI: eplan_property_set"), (committed.GetProperty("execution").GetString(),
                committed.GetProperty("changed_count").GetInt32(), committed.GetProperty("undo").GetString()));
            JsonElement actual = await ReadAsync(baseline.Changes);
            foreach (PropertySetChange change in baseline.Changes)
            {
                JsonElement row = Assert.Single(actual.EnumerateArray(), r => r.GetProperty("property_id").GetInt32() == change.Property.PropertyId
                    && r.GetProperty("target").GetProperty("locator").GetProperty("oid").GetInt64() == change.Target.Locator!.Oid);
                Assert.Equal("value", row.GetProperty("status").GetString());
                Assert.Equal(change.Value.Text, row.GetProperty(change.Property.PropertyId == 20011 ? "value" : "display").GetString());
                if (change.Property.PropertyId == 20011)
                {
                    Assert.Equal("zh_CN", row.GetProperty("language").GetString());
                    JsonElement original = Assert.Single(before.EnumerateArray(), r => r.GetProperty("property_id").GetInt32() == 20011);
                    Assert.Equal(OtherLanguages(original), OtherLanguages(row));
                }
            }
        }
        finally
        {
            JsonElement restored = await host.CallAsync("eplan_property_set", Args(restore));
            Assert.True(restored.GetProperty("execution").GetString() is "committed" or "not_started");
            Assert.True(JsonElement.DeepEquals(before, await ReadAsync(baseline.Changes)));
        }

        async Task<JsonElement> ReadAsync(List<PropertySetChange> changes)
        {
            JsonElement result = await host.CallAsync("eplan_property_get", new()
            {
                ["project"] = host.Writable,
                ["queries"] = changes.Select(c => new { target = new { locator = c.Target.Locator }, properties = new[] { new { property_id = c.Property.PropertyId } } }).ToArray(),
                ["language"] = "zh_CN",
                ["response_format"] = "detailed",
            });
            return result.GetProperty("items");
        }

        Dictionary<string, object?> Args(List<PropertySetChange> changes, bool dryRun = false) => new()
        {
            ["project"] = host.Writable,
            ["changes"] = changes.Select(c =>
            {
                var input = new Dictionary<string, object?>
                {
                    ["target"] = new { locator = c.Target.Locator },
                    ["property"] = new { property_id = c.Property.PropertyId },
                    ["value"] = c.Value.Text,
                };
                if (c.Language is not null)
                {
                    input["language"] = c.Language;
                }
                return input;
            }).ToArray(),
            ["dry_run"] = dryRun,
            ["response_format"] = "detailed",
        };
    }

    private static string[] OtherLanguages(JsonElement row) => [.. row.GetProperty("texts").EnumerateArray()
        .Where(t => t.GetProperty("language").GetString() != "zh_CN").Select(t => t.GetRawText()).Order(StringComparer.Ordinal)];
}
