using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Project/Page public accessors and runtime definitions on the unchanged sample fixture (A42 gates).</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PropertyReadTests(LiveEplan eplan)
{
    [Fact]
    public async Task ProjectProperties_EngineeringFields_CopyRuntimeDefinitionFacts()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, LiveProperties.Args(eplan, true, 10013, 10014));
        LiveProperties.Describe(rows);

        Assert.Equal([10013, 10014], rows.Select(r => r.PropertyId));
        Assert.Contains(rows, r => r.Status == "value");
        foreach (PropertyReadRow row in rows)
        {
            Assert.True(row.Status is "value" or "empty");
            PropertyDefinitionMetadata definition = Assert.IsType<PropertyDefinitionMetadata>(row.Definition);
            Assert.False(definition.IsInternal);
            Assert.False(definition.IsIndexed);
            Assert.NotNull(definition.IsReadOnly);
            Assert.NotNull(definition.IsNamePart);
            Assert.NotNull(definition.IsPropertyLicensed);
            Assert.True(definition.ValueType is "text" or "multi_language");
            Assert.Null(definition.Unit);
            Assert.Null(definition.Format);
        }
    }

    [Theory]
    [InlineData(true, 10901)]
    [InlineData(false, 11901)]
    public async Task SupplementaryFields_ExistingIndexes_MatchExplicitReads(bool project, int id)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        PropertyReadArgs args = LiveProperties.Args(eplan, project, id);
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge, args);
        LiveProperties.Describe(rows);

        Assert.NotEmpty(rows);
        if (rows is [{ Index: null } only])
        {
            // An indexed property without any stored index reads as one empty row (the sample page's 11901, A42).
            Assert.Equal("empty", only.Status);
            Assert.True(only.Definition!.IsIndexed);
            return;
        }

        Assert.All(rows, row => Assert.NotNull(row.Index));
        Assert.Equal(rows.Select(r => r.Index).Order(), rows.Select(r => r.Index));
        Assert.Equal(rows.Count, rows.Select(r => r.Index).Distinct().Count());
        foreach (PropertyReadRow row in rows)
        {
            Assert.True(row.Definition!.IsIndexed);
            Assert.NotNull(row.Definition.MaximumIndex);
            Assert.True(row.Index <= row.Definition.MaximumIndex);
            args.Properties = [new PropertySelector(propertyId: id, index: row.Index)];
            PropertyReadRow explicitRow = Assert.Single(await LiveProperties.ReadAsync(bridge, args));
            Assert.Equal((row.Index, row.Status, row.Value?.ValueType, row.Value?.Text),
                (explicitRow.Index, explicitRow.Status, explicitRow.Value?.ValueType, explicitRow.Value?.Text));
        }
    }

    [Theory]
    [InlineData("zh_CN", "400/230 V 电源")]
    [InlineData("en_US", null)]
    public async Task PageDescription_ExplicitLanguage_PreservesAllStoredTranslations(string language, string? expected)
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        PropertyReadArgs args = LiveProperties.Args(eplan, false, 11011);
        args.Language = language;
        PropertyReadRow row = Assert.Single(await LiveProperties.ReadAsync(bridge, args));

        Assert.Equal("value", row.Status);
        Assert.Equal("multi_language", row.Value!.ValueType);
        Assert.Equal(language, row.Language);
        Assert.False(row.Fallback);
        Assert.Equal(row.Value.Text, Assert.Single(row.Texts!, t => t.Language == language).Text);
        Assert.Contains(row.Texts!, t => t.Language == "de_DE");
        Assert.Contains(row.Texts!, t => t.Language == "zh_CN");
        if (expected is not null)
        {
            Assert.Equal(expected, row.Value.Text);
        }

        args.Detailed = false;
        PropertyReadRow concise = Assert.Single(await LiveProperties.ReadAsync(bridge, args));
        Assert.Equal(row.Value.Text, concise.Value!.Text);
        Assert.Null(concise.Texts);
        Assert.Null(concise.Definition);
        LiveProperties.Describe([row]);
    }

    [Fact]
    public async Task PageDescription_OmittedLanguage_UsesAndReportsActualFallback()
    {
        // Search only the sample's page descriptions, never every property. A12's first page has Chinese,
        // so it alone cannot prove the fallback branch when the GUI is Chinese.
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        PropertyReadArgs args = LiveProperties.Args(eplan, false, 11011);
        args.Language = null;
        PropertyReadRow? row = null;
        int offset = 0;
        ListData<PageSummary> pages;
        do
        {
            pages = await bridge.InvokeAsync<PageListArgs, ListData<PageSummary>>(OperationNames.PageList,
                new PageListArgs { Project = eplan.ProjectPath, Limit = 200, Offset = offset, Language = "en_US" },
                TestContext.Current.CancellationToken);
            foreach (PageSummary page in pages.Items)
            {
                args.Target = new PropertyTarget(page: page.Name);
                PropertyReadRow candidate = Assert.Single(await LiveProperties.ReadAsync(bridge, args));
                if (candidate.Fallback == true)
                {
                    row = candidate;
                    break;
                }
            }

            offset += pages.Items.Count;
        }
        while (row is null && pages.Items.Count > 0 && offset < pages.Total);

        Assert.True(row is not null, "Fallback gate needs a GUI language absent from at least one sample page's translations; do not modify the fixture.");
        LiveProperties.Describe([row]);

        Assert.Equal("value", row.Status);
        Assert.True(row.Fallback);
        Assert.NotNull(row.Language);
        Assert.Equal(row.Value!.Text, Assert.Single(row.Texts!, t => t.Language == row.Language).Text);
        args.Language = row.Language;
        PropertyReadRow explicitRow = Assert.Single(await LiveProperties.ReadAsync(bridge, args));
        Assert.Equal(row.Value.Text, explicitRow.Value!.Text);
        Assert.False(explicitRow.Fallback);
    }

    [Fact]
    public async Task PageDescription_ExplicitMissingLanguage_DoesNotUseAnotherTranslation()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PropertyReadArgs args = LiveProperties.Args(eplan, false, 11011);
        PropertyReadRow all = Assert.Single(await LiveProperties.ReadAsync(bridge, args));
        string[] candidates = ["ja_JP", "ko_KR", "fi_FI", "pt_BR", "fr_FR"];
        string absent = candidates
            .First(code => all.Texts!.All(t => t.Language != code));
        Assert.DoesNotContain(all.Texts!, t => t.Language == "??_??");
        args.Language = absent;
        PropertyReadRow row = Assert.Single(await LiveProperties.ReadAsync(bridge, args));

        Assert.Equal("empty", row.Status);
        Assert.Null(row.Value);
        Assert.Null(row.Language);
        Assert.False(row.Fallback);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefusedUserProperties_ReturnNoValueOrDefinition(bool project)
    {
        // Small's counting adapter proves no getter is called. Large checks that same refusal branch through
        // the real router on both native targets, including an unclassified numeric ID.
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge,
            LiveProperties.Args(eplan, project, 3010, 3011, 3012, 3013, int.MaxValue));

        Assert.Equal(5, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("read_refused", row.Status);
            Assert.NotNull(row.Reason);
            Assert.Null(row.Value);
            Assert.Null(row.Definition);
            Assert.Null(row.Display);
            Assert.Null(row.Texts);
        });
    }

    [Fact]
    public async Task EmptyRuntimeProperties_AreDistinguishedFromValues()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        // Bounded candidates, never a whole-list scan. On the sample page every candidate exists at runtime (A42), so no
        // not_applicable row is expected here; one is never invented for an empty value.
        List<PropertyReadRow> rows = await LiveProperties.ReadAsync(bridge,
            LiveProperties.Args(eplan, false, 11005, 11006, 11015, 11031, 11032, 11049, 11051, 11071, 25020, 25101));
        LiveProperties.Describe(rows);

        Assert.Contains(rows, r => r.Status == "empty");
        Assert.Contains(rows, r => r.Status == "value");
        Assert.All(rows, row => Assert.True(row.Status is "value" or "empty", row.Status));
        Assert.All(rows.Where(r => r.Status == "empty"), row => Assert.Null(row.Value));
    }

    [Fact]
    public async Task ExactMissingPage_ReturnsPageNotFound()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PropertyReadArgs args = LiveProperties.Args(eplan, false, 11011);
        args.Target = new PropertyTarget(page: LiveProperties.PowerSupplyPage + "-absent");

        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => LiveProperties.ReadAsync(bridge, args));

        Assert.Equal(ErrorReason.PageNotFound, error.Error.Reason);
    }
}
