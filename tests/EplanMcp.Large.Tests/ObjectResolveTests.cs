using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// object.resolve after real changes on the write copy through <see cref="DestructiveHost"/>
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.6, 7; README.md): two new pages are taken into a snapshot; one is
/// renumbered (same ObjectIdentifier, new name: stale by name, never pointed elsewhere), the other deleted (stale, not
/// found). The test deletes its pages again.
/// </summary>
[Trait("Size", "Large")]
public sealed class ObjectResolveTests : IClassFixture<DestructiveHost>
{
    private readonly LiveEplan _eplan;
    private readonly DestructiveHost _host;

    public ObjectResolveTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        (_eplan, _host) = (eplan, host);
    }

    [Fact]
    public async Task ARenumberedPage_IsStaleByName_AndADeletedOne_IsNotFound()
    {
        string[] pages = await _host.NewPagesAsync(2);
        string renumbered = DestructiveHost.Prefix + (DestructiveHost.Number(pages[1]) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(_eplan.ProcessId);
            LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = _host.Writable, Pages = [.. pages] });
            ObjectLocator[] locators = [.. pages.Select(name => snapshot.Page(name).Objects[0]).Select(o => new ObjectLocator { Project = snapshot.Chunks[0].ProjectPath, Kind = o.Kind, Type = o.Type, Oid = o.Oid, Name = o.Name })];
            Assert.All(await ResolveAsync(bridge, locators), r => Assert.Equal(LocatorResult.Resolved, r.Status));

            _host.Approvals.Approve("eplan_page_renumber", _host.Writable, ["renumber page " + pages[0]]);
            JsonElement result = await _host.CallAsync("eplan_page_renumber", new() { ["project"] = _host.Writable, ["pages"] = DestructiveHost.Json(new[] { pages[0] }), ["start"] = DestructiveHost.Number(renumbered) });
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            await _host.DeleteAsync([pages[1]]);
            List<LocatorResult> after = await ResolveAsync(bridge, locators);

            Assert.Equal((LocatorResult.Stale, "name", renumbered), (after[0].Status, after[0].Reason, after[0].ActualName));
            Assert.Equal((LocatorResult.Stale, "not_found", (string?)null), (after[1].Status, after[1].Reason, after[1].ActualName));
            Assert.All(after, r => Assert.Null(r.Hash));
            await _host.DeleteAsync([renumbered]);
        }
        catch
        {
            await _host.RemoveQuietlyAsync([.. pages, renumbered]);
            throw;
        }
    }

    private static async Task<List<LocatorResult>> ResolveAsync(BridgeClient bridge, ObjectLocator[] locators) =>
        (await bridge.InvokeAsync<ObjectResolveArgs, ObjectResolveData>(OperationNames.ObjectResolve, new ObjectResolveArgs { Locators = [.. locators] }, TestContext.Current.CancellationToken)).Results;
}
