using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PageTextReplaceTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private const string Page = "=F+A2&EFA1/2";

    [Fact]
    public async Task PageFreeText_DryRunReplaceReadbackAndOneUndo_RestoreOriginalWithoutChangingFramesOrOtherPages()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        Assert.StartsWith(WriteHost.ScratchFolder + Path.DirectorySeparatorChar, host.Writable, StringComparison.OrdinalIgnoreCase);
        // A41/A46: this closes only known fixture copies to select LargeWrite, checks current before undo,
        // and reopens only the copies it actually closed. It never reopens an already open LargeWrite.
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        LiveSnapshots.Snapshot standard = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
        string[] pages = [.. standard.Pages.Select(p => p.Name)];
        Dictionary<string, PageTextInspection> before = await InspectAsync(bridge, pages);
        Assert.True(before.ContainsKey(Page), "LargeWrite must contain the S38 free-text sample page.");
        PageTextInspection target = before[Page];
        Assert.True(target.Texts.Count > 0, "The page must contain page-origin free texts.");
        Assert.True(target.FrameTextCount > 0, "The fixture must contain plot-frame texts to verify their exclusion.");
        var candidates = target.Texts.OrderBy(t => t.Key).SelectMany(t => t.Value.OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => (Oid: t.Key, Language: e.Key, Before: e.Value)));
        var candidate = candidates.FirstOrDefault(c => c.Before.Length is > 0 and <= 440 &&
            target.Texts.Where(t => t.Key != c.Oid).All(t => !t.Value.TryGetValue(c.Language, out string? value) || !value.Contains(c.Before, StringComparison.Ordinal)));
        Assert.True(candidate.Before is not null, "Prepare a unique free-text language entry of 1-440 characters in LargeWrite.");
        string replacement = "[page-text-" + Guid.NewGuid().ToString("N") + "]" + candidate.Before;
        string baseline = State(before);
        int auditBefore = host.AuditRecords().Count;

        JsonElement dryRun = await host.CallAsync("eplan_page_text_replace", Args(candidate.Before, replacement, candidate.Language, dryRun: true));
        Assert.Equal(("not_started", 1, 1), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("totalChanges").GetInt32(), dryRun.GetProperty("totalOccurrences").GetInt32()));
        Assert.Equal(candidate.Oid, Assert.Single(dryRun.GetProperty("changes").EnumerateArray()).GetProperty("oid").GetInt64());
        Assert.Equal(baseline, State(await InspectAsync(bridge, pages)));
        Assert.Equal(auditBefore, host.AuditRecords().Count);

        bool committed = false;
        bool undoAttempted = false;
        bool undoReturned = false;
        try
        {
            JsonElement result = await host.CallAsync("eplan_page_text_replace", Args(candidate.Before, replacement, candidate.Language));
            committed = result.GetProperty("execution").GetString() == "committed";
            Assert.True(committed);
            Assert.Equal("AI: eplan_page_text_replace", result.GetProperty("undo").GetString());
            Assert.False(result.GetProperty("recovered").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("hint").ValueKind);
            Dictionary<string, PageTextInspection> after = await InspectAsync(bridge, pages);
            AssertOnlyChange(before, after, candidate.Oid, candidate.Language, replacement);
            // The standard snapshot independently covers page/function/connection properties, but not free texts.
            Assert.Equal(standard.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);

            undoAttempted = true;
            await selection.UndoAsync();
            undoReturned = true;
            Assert.Equal(baseline, State(await InspectAsync(bridge, pages)));
            committed = false;
            Assert.Equal(standard.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);
        }
        finally
        {
            // No retry of undo or compensation after an unknown write/undo outcome. For a confirmed commit,
            // restore only if independent evidence proves the unique marker is still the sole text change.
            if (committed && (!undoAttempted || undoReturned))
            {
                Dictionary<string, PageTextInspection> current = await InspectAsync(bridge, pages);
                if (State(current) != baseline)
                {
                    AssertOnlyChange(before, current, candidate.Oid, candidate.Language, replacement);
                    Assert.Equal(standard.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);
                    JsonElement restored = await host.CallAsync("eplan_page_text_replace", Args(replacement, candidate.Before, candidate.Language));
                    Assert.Equal("committed", restored.GetProperty("execution").GetString());
                }

                Assert.Equal(baseline, State(await InspectAsync(bridge, pages)));
            }
        }
    }

    private Dictionary<string, object?> Args(string find, string replacement, string language, bool dryRun = false) => new()
    {
        ["project"] = host.Writable,
        ["pages"] = new[] { Page },
        ["find"] = find,
        ["replace"] = replacement,
        ["language"] = language,
        ["max_changes"] = 1,
        ["dry_run"] = dryRun,
    };

    private async Task<Dictionary<string, PageTextInspection>> InspectAsync(BridgeClient bridge, string[] pages)
    {
        var result = new List<PageTextInspection>();
        foreach (string[] chunk in pages.Chunk(50))
        {
            result.AddRange(await bridge.InvokeAsync<ProjectSnapshotArgs, List<PageTextInspection>>(OperationNames.PageTextInspect,
                new() { Project = host.Writable, Pages = [.. chunk] }, TestContext.Current.CancellationToken));
        }

        Assert.Equal(pages.Order(StringComparer.Ordinal), result.Select(p => p.Page).Order(StringComparer.Ordinal));
        return result.ToDictionary(p => p.Page, StringComparer.Ordinal);
    }

    private static void AssertOnlyChange(Dictionary<string, PageTextInspection> before, Dictionary<string, PageTextInspection> after,
        long oid, string language, string replacement)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, PageTextInspection original) in before)
        {
            PageTextInspection current = after[name];
            Assert.Equal(original.ProtectedHash, current.ProtectedHash);
            Assert.Equal(original.FrameTextCount, current.FrameTextCount);
            Assert.Equal(original.Texts.Keys.Order(), current.Texts.Keys.Order());
            foreach ((long textOid, Dictionary<string, string> entries) in original.Texts)
            {
                Assert.Equal(entries.Keys.Order(StringComparer.Ordinal), current.Texts[textOid].Keys.Order(StringComparer.Ordinal));
                foreach ((string code, string value) in entries)
                {
                    Assert.Equal(name == Page && textOid == oid && code == language ? replacement : value, current.Texts[textOid][code]);
                }
            }
        }
    }

    private static string State(Dictionary<string, PageTextInspection> pages) => JsonSerializer.Serialize(pages.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => new { p.Key, p.Value.ProtectedHash, p.Value.FrameTextCount, Texts = p.Value.Texts.OrderBy(t => t.Key).Select(t => new { t.Key, Entries = t.Value.OrderBy(e => e.Key, StringComparer.Ordinal) }) }));
}
