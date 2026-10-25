using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Function/Connection read-only sample gates; native facts remain unconfirmed until these run (A43).</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PropertyReadObjectTests(LiveEplan eplan)
{
    [Fact]
    public async Task DeviceTag_FunctionTextAndTechnicalCharacteristic_MatchIndependentSnapshot()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        SnapshotObject device = LivePropertyObjects.Device(snapshot);
        PropertyReadArgs args = LivePropertyObjects.Args(eplan, new PropertyTarget(deviceTag: device.Name), 20011);
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, args);
        LiveProperties.Describe(rows);

        PropertyReadRow text = Assert.Single(rows);
        Assert.Equal((20011, "value"), (text.PropertyId, text.Status));
        Assert.Equal("multi_language", text.ValueType);
        SnapshotValue independent = Assert.Single(device.Values, v => v.Id == 20011);
        Assert.Equal(independent.Texts!.Select(t => (t.Language, t.Text)), text.Texts!.Select(t => (t.Language, t.Text)));
        Assert.Equal(Assert.Single(text.Texts!, t => t.Language == text.Language).Text, text.Value!.Text);

        foreach (SnapshotText translation in independent.Texts!)
        {
            args.Language = translation.Language;
            PropertyReadRow explicitText = (await LiveProperties.ReadAsync(bridge, args))[0];
            Assert.Equal(translation.Text, explicitText.Value!.Text);
            Assert.Equal(translation.Language, explicitText.Language);
            Assert.False(explicitText.Fallback);
        }

        SnapshotObject rated = LivePropertyObjects.Device(snapshot, 20027);
        List<PropertyReadRow> ratedRows = await LiveProperties.ReadAsync(bridge,
            LivePropertyObjects.Args(eplan, new PropertyTarget(deviceTag: rated.Name), 20027));
        LiveProperties.Describe(ratedRows);
        PropertyReadRow technical = Assert.Single(ratedRows);
        Assert.Equal((20027, "value"), (technical.PropertyId, technical.Status));
        Assert.NotNull(technical.Definition);
        SnapshotValue rating = Assert.Single(rated.Values, v => v.Id == 20027);
        if (technical.ValueType == "text")
        {
            Assert.Equal(rating.Value, technical.Value!.Text);
        }
        else
        {
            // On EPLAN 2026.0.3 the runtime type of 20027 is neither String nor MultilangString (A44): only the safe
            // display text is returned, which equals the snapshot's ToString reading.
            Assert.Equal("opaque", technical.ValueType);
            Assert.Null(technical.Value);
            Assert.Equal(rating.Value, technical.Display);
        }

        args.Language = null;
        args.Target = new PropertyTarget(deviceTag: device.Name, page: device.Page);
        List<PropertyReadRow> qualified = await LiveProperties.ReadAsync(bridge, args);
        Assert.Equal(rows.Select(r => (r.Status, r.Value?.Text)), qualified.Select(r => (r.Status, r.Value?.Text)));
    }

    [Fact]
    public async Task SameFullTag_MultipleMainFunctions_ReturnsCandidateLocators()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        IGrouping<string?, SnapshotObject> sameName = snapshot.Objects("function")
            .Where(o => o.Main == true && o.Name is not null)
            .GroupBy(o => o.Name, StringComparer.Ordinal)
            .First(g => g.Select(o => (o.Type, o.Oid)).Distinct().Count() > 1);
        PropertyReadArgs args = LivePropertyObjects.Args(eplan, new PropertyTarget(deviceTag: sameName.Key), 20011);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => LiveProperties.ReadAsync(bridge, args));

        Assert.Equal(ErrorReason.AmbiguousName, error.Error.Reason);
        Assert.Null(error.Error.Execution);
        ObjectLocator[] candidates = JsonSerializer.Deserialize<ObjectLocator[]>(error.Error.Metadata!["candidates"], HostJson.Options)!;
        Assert.InRange(candidates.Length, 2, 20);
        Assert.Equal(candidates.Length, candidates.Select(c => (c.Type, c.Oid)).Distinct().Count());
        Assert.All(candidates, c =>
        {
            Assert.Equal(eplan.ProjectPath, c.Project);
            Assert.Equal("function", c.Kind);
            Assert.Equal(sameName.Key, c.Name);
            Assert.True(c.Type > 0);
            Assert.True(c.Oid > 0);
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"Ambiguous full tag {sameName.Key}: {candidates.Length} candidate locators.");

        // The finder may also supply page-less mains absent from the snapshot. Check each returned locator through
        // property.read itself; object.resolve currently only enumerates placed functions (A32/A34).
        foreach (ObjectLocator candidate in candidates)
        {
            args.Target = new PropertyTarget(locator: candidate);
            Assert.Single(await LiveProperties.ReadAsync(bridge, args));
        }
    }

    [Fact]
    public async Task ConnectionLocator_CrossSectionColourNumberAndDesignation_AreReadable()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        SnapshotObject connection = LivePropertyObjects.Connection(snapshot);
        PropertyReadArgs args = LivePropertyObjects.Args(eplan, new PropertyTarget(locator: LivePropertyObjects.Locator(eplan, connection)), 31002, 31004, 31011);
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, args);
        LiveProperties.Describe(rows);

        Assert.Equal([31002, 31004, 31011], rows.Select(r => r.PropertyId));
        Assert.All(rows, r =>
        {
            Assert.True(r.Status is "value" or "empty", r.Status);
            Assert.NotNull(r.Definition);
            Assert.False(r.Definition.IsInternal);
            Assert.NotNull(r.Definition.IsPropertyLicensed);
        });
        ListData<ConnectionListItem> listed = await bridge.InvokeAsync<ConnectionListArgs, ListData<ConnectionListItem>>(OperationNames.ConnectionList,
            new ConnectionListArgs { Project = eplan.ProjectPath, Page = connection.Page, Limit = 200 }, TestContext.Current.CancellationToken);
        ConnectionListItem independent = Assert.Single(listed.Items, c => c.From?.Device == connection.Start?.Function
            && c.From?.Pin == connection.Start?.Pin && c.To?.Device == connection.End?.Function && c.To?.Pin == connection.End?.Pin);
        Assert.Equal(independent.CrossSection, rows[0].Value?.Text ?? rows[0].Display);
        Assert.Equal(independent.Colour, rows[1].Value?.Text ?? rows[1].Display);
        // Colour/number retains the stored form; this test grants no write or unit conversion permission.
        Assert.Equal("value", rows[0].Status);
        Assert.Equal("value", rows[1].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleFunctionOrConnectionLocator_IsRefusedWithoutNameReresolution(bool connection)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        SnapshotObject obj = connection ? LivePropertyObjects.Connection(snapshot) : LivePropertyObjects.Device(snapshot);
        ObjectLocator locator = LivePropertyObjects.Locator(eplan, obj);
        locator.Name += "-stale";
        PropertyReadArgs args = LivePropertyObjects.Args(eplan, new PropertyTarget(locator: locator), connection ? 31002 : 20011);

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => LiveProperties.ReadAsync(bridge, args));

        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Equal("name", error.Error.Metadata!["check"]);
        Assert.Null(error.Error.Execution);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedProperty_FunctionOrConnection_ReturnsNoValueViews(bool connection)
    {
        // Counting Small tests prove that no native accessor can be reached; this runs the same branch on real targets.
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LivePropertyObjects.SnapshotAsync(bridge, eplan);
        SnapshotObject obj = connection ? LivePropertyObjects.Connection(snapshot) : LivePropertyObjects.Device(snapshot);
        PropertyReadArgs args = LivePropertyObjects.Args(eplan, new PropertyTarget(locator: LivePropertyObjects.Locator(eplan, obj)),
            3010, 3011, 3012, 3013, 2000, int.MaxValue, connection ? 20011 : 31002);

        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, args);

        Assert.Equal(7, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("read_refused", row.Status);
            Assert.NotNull(row.Reason);
            Assert.Null(row.Value);
            Assert.Null(row.Definition);
            Assert.Null(row.Display);
            Assert.Null(row.Texts);
            Assert.Null(row.Language);
        });
    }
}
