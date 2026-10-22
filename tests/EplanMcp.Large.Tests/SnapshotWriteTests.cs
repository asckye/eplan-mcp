using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.snapshot around a change on the write copy (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.5, 7; README.md):
/// a new page description changes that page's hash and modification date only (S31's marker test, now through the
/// snapshot), and the page taken alone has the hash it has in the whole snapshot.
/// </summary>
[Trait("Size", "Large")]
public sealed class SnapshotWriteTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private const string Page = "=GAA+A1&EFS1/1";

    [Fact]
    public async Task ANewPageDescription_ChangesOnlyThatPagesHashAndModificationDate()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = host.Writable });
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        await host.CallAsync("eplan_page_update", new() { ["project"] = host.Writable, ["pages"] = JsonSerializer.SerializeToElement(new[] { new { name = Page, description = "Snapshot " + stamp } }), ["language"] = "zh_CN" });
        LiveSnapshots.Snapshot after = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = host.Writable });
        LiveSnapshots.Snapshot alone = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = host.Writable, Pages = [Page] });

        TestContext.Current.TestOutputHelper?.WriteLine(after.Describe());
        Dictionary<long, SnapshotPage> old = before.Pages.ToDictionary(p => p.Oid);
        Assert.Equal([Page], after.Pages.Where(p => old[p.Oid].Hash != p.Hash).Select(p => p.Name));
        Assert.Equal([Page], after.Pages.Where(p => old[p.Oid].Modified != p.Modified).Select(p => p.Name));
        Assert.NotEqual(before.Hash, after.Hash);
        Assert.Equal(after.Page(Page).Hash, alone.Page(Page).Hash);
        SnapshotValue description = Assert.Single(after.Page(Page).Objects[0].Values, v => v.Name == "PAGE_NOMINATIOMN");
        Assert.Contains(description.Texts!, t => t.Language == "zh_CN" && t.Text == "Snapshot " + stamp);
        TestContext.Current.TestOutputHelper?.WriteLine($"{Page}: modified {old[after.Page(Page).Oid].Modified} -> {after.Page(Page).Modified}");
    }
}
