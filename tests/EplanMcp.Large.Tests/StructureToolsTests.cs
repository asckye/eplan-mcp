using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_structure_list, _update and _delete on LargeWrite: one call for a batch, read back, one undo each (api-notes A106).</summary>
[Trait("Size", "Large")]
public sealed class StructureToolsTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private readonly string _run = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private string Mounting => "+MCPT" + _run;
    private string Document => "&MCPT" + _run;
    private string Nested => Mounting + ".SUB";

    [Fact]
    public async Task Batch_CreatesAndDescribes_ListsBack_OneUndoRestores_ThenDeletesUnused()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        Dictionary<string, Row> before = await ListAsync(session.Project);
        Assert.NotEmpty(before);
        Assert.Contains(before.Values, r => r.Used);
        Assert.DoesNotContain(before.Keys, name => name is "=" or "+" or "&");

        string used = before.Values.First(r => r.Used && !r.Name.Contains('.', StringComparison.Ordinal)).Name;
        string oldDescription = before[used].Description;
        const string newDescription = "zh_CN@测试;en_US@Test;";
        var args = new Dictionary<string, object?>
        {
            ["project"] = session.Project,
            ["identifiers"] = new object[]
            {
                new { name = Mounting, description = "zh_CN@测试柜;en_US@Test cabinet;" },
                new { name = Nested, description = "en_US@Nested;" },
                new { name = Document },
                new { name = used, description = newDescription },
            },
        };
        JsonElement preview = await eplan.CallAsync("eplan_structure_update", args);
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        Assert.Equal(["create", "create", "create", "update"], preview.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("action").GetString()));
        Assert.Equal(before.Count, (await ListAsync(session.Project)).Count);

        JsonElement set = await eplan.CallAsync("eplan_structure_update", new(args) { ["dry_run"] = false });
        Assert.Equal("committed", set.GetProperty("execution").GetString());
        Dictionary<string, Row> after = await ListAsync(session.Project);
        Assert.Contains("en_US@Test cabinet;", after[Mounting].Description, StringComparison.Ordinal);
        Assert.Contains("en_US@Nested;", after[Nested].Description, StringComparison.Ordinal);
        Assert.Contains("en_US@Test;", after[used].Description, StringComparison.Ordinal);
        Assert.Equal((true, false, false), (after[Mounting].Used, after[Nested].Used, after[Document].Used)); // a sub-identifier counts as a use of its parent
        Assert.True(after[used].Used);

        // Nothing left to do: the same call plans no change.
        JsonElement again = await eplan.CallAsync("eplan_structure_update", args);
        Assert.Empty(again.GetProperty("changes").EnumerateArray());

        await session.UndoAsync();
        Dictionary<string, Row> undone = await ListAsync(session.Project);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), undone.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(oldDescription, undone[used].Description);

        // Create them again, then delete: a used one is refused, the unused ones go, and one undo brings them back.
        await eplan.CallAsync("eplan_structure_update", new(args) { ["identifiers"] = new object[] { new { name = Mounting }, new { name = Document } }, ["dry_run"] = false });
        JsonElement refused = await host.ErrorAsync("eplan_structure_delete", new() { ["project"] = host.Writable, ["identifiers"] = new[] { used } });
        Assert.Equal("INVALID_ARGUMENT", refused.GetProperty("reason").GetString());

        var deletion = new Dictionary<string, object?> { ["project"] = host.Writable, ["identifiers"] = new[] { Mounting, Document } };
        JsonElement planned = await host.CallAsync("eplan_structure_delete", deletion);
        Assert.Equal("not_started", planned.GetProperty("execution").GetString());
        Assert.Equal([Mounting, Document], planned.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        host.Approvals.Approve("eplan_structure_delete", host.Writable, new[] { "delete structure " + Mounting, "delete structure " + Document });
        deletion["dry_run"] = false;
        JsonElement deleted = await host.CallAsync("eplan_structure_delete", deletion);
        Assert.Equal("committed", deleted.GetProperty("execution").GetString());
        Assert.True(File.Exists(deleted.GetProperty("backup").GetString()));
        Dictionary<string, Row> gone = await ListAsync(session.Project);
        Assert.DoesNotContain(Mounting, gone.Keys);
        Assert.DoesNotContain(Document, gone.Keys);
        await session.UndoAsync();
        Assert.Contains(Mounting, (await ListAsync(session.Project)).Keys);

        // Leaves the copy as it was for the other tests. A further undo here raised EPLAN's undo conflict dialog (A106), so these go by a second delete.
        host.Approvals.Approve("eplan_structure_delete", host.Writable, new[] { "delete structure " + Mounting, "delete structure " + Document });
        await host.CallAsync("eplan_structure_delete", deletion);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), (await ListAsync(session.Project)).Keys.Order(StringComparer.Ordinal));
    }

    private async Task<Dictionary<string, Row>> ListAsync(string project)
    {
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            JsonElement read = await eplan.CallAsync("eplan_structure_list", new() { ["project"] = project, ["limit"] = 500, ["cursor"] = cursor });
            foreach (JsonElement i in read.GetProperty("identifiers").EnumerateArray())
            { rows.Add(i.GetProperty("name").GetString()!, new Row(i.GetProperty("name").GetString()!, i.GetProperty("description").GetString()!, i.GetProperty("used").GetBoolean())); }
            cursor = read.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        return rows;
    }

    private sealed record Row(string Name, string Description, bool Used);
}
