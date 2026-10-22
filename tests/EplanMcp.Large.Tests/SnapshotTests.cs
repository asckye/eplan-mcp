using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.snapshot of the fixture (docs/design/2026-10-21-m6-bridge-snapshot.md, 7; README.md): two snapshots in a row
/// are identical (S31 found it within one opening), the counts are S31's, the motor =+-CU1 has S28's identity, and
/// reading changes no project file (A36).
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class SnapshotTests(LiveEplan eplan)
{
    [Fact]
    public async Task TwoSnapshotsInARow_AreIdentical_AndHaveTheSampleProjectsCounts()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);

        LiveSnapshots.Snapshot first = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });
        LiveSnapshots.Snapshot second = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });

        TestContext.Current.TestOutputHelper?.WriteLine(first.Describe());
        TestContext.Current.TestOutputHelper?.WriteLine(second.Describe());
        Assert.Equal(first.Pages.Select(p => (p.Oid, p.Hash)), second.Pages.Select(p => (p.Oid, p.Hash)));
        Assert.Equal(first.Hash, second.Hash);
        if (first.Chunks.Count == 1)
        {
            Assert.Equal(first.Hash, first.Chunks[0].SnapshotHash); // the Bridge's hash and the Host's formula agree
        }

        // A12, S31 (light snapshot of the same fixture): 321 pages, 3,920 functions placed on pages, 1,302 connections
        // on pages. S31 did not leave out 3D connections; if only the connection count differs, compare that first.
        Assert.Equal(321, first.Pages.Count);
        Assert.Equal(3920, first.Objects("function").Count());
        Assert.Equal(1302, first.Objects("connection").Count());
        Assert.True(first.Pages.Select(p => p.Oid).SequenceEqual(first.Pages.Select(p => p.Oid).Order()), "Pages must come in ascending order of their numbers.");
        Assert.Equal(files, LiveSnapshots.DesignFiles(eplan.ProjectPath));

        // Not read live before this test (design 8, item 2): what these values look like.
        foreach (string name in new[] { "PAGE_FORMPLOT", "FUNC_TECHNICAL_CHARACTERISTIC" })
        {
            string[] samples = [.. first.Pages.SelectMany(p => p.Objects).SelectMany(o => o.Values).Where(v => v.Name == name).Select(v => v.Value ?? "(texts)").Distinct().Take(5)];
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {string.Join(" | ", samples)}");
        }
    }

    [Fact]
    public async Task TheMotorCU1_HasTheIdentityS28Found_AndOnePageAloneHasTheSameHash()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot whole = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });

        SnapshotObject motor = Assert.Single(whole.Objects("function"), o => o.Name == "=+-CU1" && o.Main == true);
        Assert.Equal((17, 59641L), (motor.Type, motor.Oid)); // S28: 28/17/59641/0 in both copies
        LiveSnapshots.Snapshot page = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath, Pages = [motor.Page!, "=NO+SUCH/1"] });
        Assert.Equal(["=NO+SUCH/1"], page.Chunks[0].MissingPages);
        Assert.Equal(whole.Page(motor.Page!).Hash, page.Page(motor.Page!).Hash);
        Assert.Null(page.Chunks[0].SnapshotHash);
        TestContext.Current.TestOutputHelper?.WriteLine($"=+-CU1 on {motor.Page} at {motor.Pos}, PROJ_GUID {whole.Chunks[0].ProjectGuid}, modified {whole.Chunks[0].Modified}");
    }

    [Fact]
    public async Task Resolve_TheMotorCU1_IsResolvedWithItsSnapshotHash_AndAWrongDescriptionIsStale()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot whole = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });
        SnapshotObject motor = Assert.Single(whole.Objects("function"), o => o.Name == "=+-CU1" && o.Main == true);
        SnapshotObject page = whole.Page(motor.Page!).Objects[0];
        SnapshotObject connection = whole.Objects("connection").First();
        ObjectLocator Locator(SnapshotObject o) => new() { Project = whole.Chunks[0].ProjectPath, Kind = o.Kind, Type = o.Type, Oid = o.Oid, Name = o.Name, Page = o.Page, Category = o.Category, Pos = o.Pos };
        ObjectLocator renamed = Locator(motor);
        renamed.Name = "=+-CU9";
        ObjectLocator moved = Locator(motor);
        moved.Page = "=NO+SUCH/1";
        ObjectLocator shifted = Locator(motor);
        shifted.Pos = Shifted(motor.Pos!, 0.01);
        ObjectLocator gone = Locator(motor);
        gone.Oid = 999_999_999;

        ObjectResolveData data = await bridge.InvokeAsync<ObjectResolveArgs, ObjectResolveData>(
            OperationNames.ObjectResolve,
            new ObjectResolveArgs { Project = eplan.ProjectPath, Locators = [Locator(motor), Locator(page), Locator(connection), renamed, moved, shifted, gone] },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [(LocatorResult.Resolved, null, motor.Hash), (LocatorResult.Resolved, null, page.Hash), (LocatorResult.Resolved, null, connection.Hash)],
            data.Results.Take(3).Select(r => (r.Status, r.Reason, r.Hash)));
        Assert.Equal(["name", "page", "position", "not_found"], data.Results.Skip(3).Select(r => r.Reason));
        Assert.Equal(("=+-CU1", motor.Page), (data.Results[3].ActualName, data.Results[4].ActualPage));
    }

    /// <summary>A position <paramref name="pos"/> moved by <paramref name="offset"/> millimetres in x.</summary>
    private static string Shifted(string pos, double offset)
    {
        string[] xy = pos.Split(',');
        return (double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture) + offset).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + xy[1];
    }
}
