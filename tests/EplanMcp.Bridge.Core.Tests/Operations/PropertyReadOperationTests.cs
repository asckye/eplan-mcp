using System.Globalization;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyReadOperationTests
{
    internal static PropertyReadArgs Args(params int[] ids) => new()
    {
        ProjectPath = "resolved-project",
        Target = new PropertyTarget(projectObject: true),
        Properties = ids.Length == 0 ? [new PropertySelector(propertyId: 10013)] : [.. ids.Select(id => new PropertySelector(propertyId: id))],
    };

    internal static PropertyReadData Read(FakePropertyReadAdapter adapter, PropertyReadArgs? args = null, Func<TimeSpan>? elapsed = null) =>
        (PropertyReadData)((IOperation)new PropertyReadOperation(adapter, elapsed)).Execute(args ?? Args())!;

    internal static FakePropertyReadAdapter Adapter(FakeProperty? property = null)
    {
        var adapter = new FakePropertyReadAdapter();
        adapter.Properties.Add(10013, property ?? new FakeProperty());
        return adapter;
    }

    [Theory]
    [InlineData(3010)]
    [InlineData(3011)]
    [InlineData(3012)]
    [InlineData(3013)]
    [InlineData(2000)]
    [InlineData(int.MaxValue)]
    public void Read_RefusedCatalogId_NeverOpensPropertyOrReadsAnyValue(int id)
    {
        FakePropertyReadAdapter adapter = Adapter();
        adapter.OnAccess = stage => Assert.False(stage is "property.lookup" or "default_language" or "display" or "is_empty");

        PropertyReadRow row = Assert.Single(Read(adapter, Args(id)).Items);

        Assert.Equal("read_refused", row.Status);
        Assert.Null(row.Value);
        Assert.Equal(0, adapter.PropertyOpens);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void Read_InternalOrUnknownIndexedness_RefusesBeforeValueAccess(bool? internalProperty)
    {
        var property = new FakeProperty();
        property.Metadata.IsInternal = internalProperty;
        property.Metadata.IsIndexed = internalProperty is null ? null : false;
        FakePropertyReadAdapter adapter = Adapter(property);

        Assert.Equal("read_refused", Assert.Single(Read(adapter).Items).Status);

        Assert.Empty(adapter.ValueRequests);
        Assert.DoesNotContain("is_empty", adapter.Events);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("missing", "not_applicable")]
    [InlineData("value_missing", "not_applicable")]
    [InlineData("empty", "empty")]
    [InlineData("set", "value")]
    public void Read_ApplicabilityAndEmptiness_AreSeparateStatuses(string state, string expected)
    {
        FakePropertyReadAdapter adapter = Adapter(new FakeProperty { Empty = state == "empty", ValueApplicable = state != "value_missing" });
        if (state == "missing")
        {
            adapter.Properties.Clear();
        }

        PropertyReadRow row = Assert.Single(Read(adapter).Items);

        Assert.Equal(expected, row.Status);
        Assert.Equal(expected == "value" ? 1 : 0, adapter.StoredReads);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_UnknownInternalFact_RefusesEvenWithKnownIndexedness()
    {
        var property = new FakeProperty();
        property.Metadata.IsInternal = null;
        property.Metadata.IsIndexed = false;
        FakePropertyReadAdapter adapter = Adapter(property);
        PropertyReadArgs args = Args();
        args.Detailed = true;

        PropertyReadRow row = Assert.Single(Read(adapter, args).Items);

        Assert.Equal("read_refused", row.Status);
        Assert.Equal("unknown_internal", row.Reason);
        Assert.Null(row.Definition);
        Assert.Empty(adapter.ValueRequests);
        Assert.DoesNotContain("is_empty", adapter.Events);
        adapter.AssertDisposed();
    }

    public static TheoryData<string, object> TypedValues => new()
    {
        { "text", "1,5" },
        { "boolean", true },
        { "integer", long.MaxValue },
        { "number", 1.5d },
        { "datetime", new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(2)) },
        { "enum", "Engineering" },
        { "enum", 17L },
    };

    [Theory]
    [MemberData(nameof(TypedValues))]
    public void Read_TypedStorage_PreservesTypeAndSeparatesDisplay(string type, object stored)
    {
        var property = new FakeProperty { Stored = stored, Display = "1,5 mm" };
        property.Metadata.ValueType = type;
        property.Metadata.Unit = "mm";
        property.Metadata.Format = "verified-format";
        property.Metadata.IsReadOnly = true;
        FakePropertyReadAdapter adapter = Adapter(property);
        PropertyReadArgs args = Args();
        args.Detailed = true;

        PropertyReadRow row = Assert.Single(Read(adapter, args).Items);

        Assert.Equal(type, row.ValueType);
        PropertyTypedValue value = Assert.IsType<PropertyTypedValue>(row.Value);
        Assert.Equal(stored, (object?)value.Text ?? (object?)value.Boolean ?? (object?)value.IntegerValue ?? (object?)value.Number ?? value.DateTime);
        Assert.Equal("1,5 mm", row.Display);
        Assert.Equal("mm", row.Unit);
        Assert.Equal("verified-format", row.Format);
        Assert.True(row.ReadOnly);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("future_native_type")]
    [InlineData(null)]
    public void Read_UnknownType_ReturnsDisplayWithoutCallingStorageGetter(string? type)
    {
        var property = new FakeProperty();
        property.Metadata.ValueType = type;
        FakePropertyReadAdapter adapter = Adapter(property);

        PropertyReadRow row = Assert.Single(Read(adapter).Items);

        Assert.Equal("value", row.Status);
        Assert.Equal("opaque", row.ValueType);
        Assert.Null(row.Value);
        Assert.Equal("display text", row.Display);
        Assert.Equal(0, adapter.StoredReads);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_IndexedProperty_UsesExistingIndexesInBlocksOfAtMost200()
    {
        var property = new FakeProperty { Indexes = [.. Enumerable.Range(0, 401).Select(i => i * 3 - 17)] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = Adapter(property);
        PropertyReadArgs args = Args();
        var all = new List<int?>();
        PropertyReadData data;
        do
        {
            data = Read(adapter, args);
            Assert.InRange(data.Items.Count, 0, 200);
            all.AddRange(data.Items.Select(r => r.Index));
            args.SelectorOffset = data.NextSelector ?? 0;
            args.IndexOffset = data.NextIndexOffset;
        }
        while (data.NextSelector.HasValue);

        Assert.Equal(property.Indexes.Select(i => (int?)i), all);
        Assert.Equal(new[] { (0, 200), (200, 200), (400, 200) }, adapter.IndexRequests);
        Assert.All(adapter.ValueRequests, r => Assert.NotNull(r.Index));
        Assert.Equal(3, adapter.Opens);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_IndexedPropertyWithNoIndexes_ReturnsEmptyWithoutCallingIsEmpty()
    {
        var property = new FakeProperty();
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = Adapter(property);

        Assert.Equal("empty", Assert.Single(Read(adapter).Items).Status);

        Assert.DoesNotContain("is_empty", adapter.Events);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_ExplicitIndex_ReadsOnlyThatIndexWithoutEnumerating()
    {
        var property = new FakeProperty();
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = Adapter(property);
        PropertyReadArgs args = Args();
        args.Properties = [new PropertySelector(propertyId: 10013, index: 7)];

        Assert.Equal(7, Assert.Single(Read(adapter, args).Items).Index);

        Assert.Empty(adapter.IndexRequests);
        Assert.Equal((10013, 7), Assert.Single(adapter.ValueRequests));
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_IndexOnScalar_FailsBeforeIsEmptyOrValueGetter()
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertyReadArgs args = Args();
        args.Properties = [new PropertySelector(propertyId: 10013, index: 0)];

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Read(adapter, args));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Empty(adapter.ValueRequests);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    public void Read_InvalidContinuation_IsRejectedBeforeOpening(int selector, int index)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertyReadArgs args = Args();
        args.SelectorOffset = selector;
        args.IndexOffset = index;

        Assert.Throws<BridgeErrorException>(() => Read(adapter, args));

        Assert.Equal(0, adapter.Opens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Read_InvalidSelectorCount_IsRejectedBeforeOpening(int count)
    {
        FakePropertyReadAdapter adapter = Adapter();
        PropertyReadArgs args = Args();
        args.Properties = [.. Enumerable.Repeat(new PropertySelector(propertyId: 10013), count)];

        Assert.Throws<BridgeErrorException>(() => Read(adapter, args));

        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Read_TextNumber_UnderGermanCultureIsNeverReparsed()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            FakePropertyReadAdapter adapter = Adapter(new FakeProperty { Stored = "1,5" });

            Assert.Equal("1,5", Assert.Single(Read(adapter).Items).Value!.Text);

            adapter.AssertDisposed();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Read_ComputedDetailedValue_UsesInjectedObservationTimeAndKeepsUnknownFactsNull()
    {
        var property = new FakeProperty();
        property.Metadata.IsComputed = true;
        FakePropertyReadAdapter adapter = Adapter(property);
        PropertyReadArgs args = Args();
        args.Detailed = true;
        var observed = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var operation = new PropertyReadOperation(adapter, utcNow: () => observed);

        PropertyReadRow row = Assert.Single(((PropertyReadData)operation.Execute(args)!).Items);

        Assert.True(row.Computed);
        Assert.Equal(observed, row.ObservedAt);
        Assert.Null(row.ReadOnly);
        Assert.Null(row.Unit);
        Assert.Null(row.Format);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Read_NonFiniteNativeNumber_IsRejectedAndResourcesAreDisposed(double number)
    {
        var property = new FakeProperty { Stored = number };
        property.Metadata.ValueType = "number";
        FakePropertyReadAdapter adapter = Adapter(property);

        Assert.Throws<ArgumentException>(() => Read(adapter));

        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_IndexedRowsAfterScalar_SharesThe200RowBlockBudget()
    {
        var property = new FakeProperty { Indexes = [.. Enumerable.Range(6, 200)] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = Adapter(property);
        adapter.Properties.Add(10014, new FakeProperty());
        PropertyReadArgs args = Args(10014, 10013);

        PropertyReadData first = Read(adapter, args);

        Assert.Equal(200, first.Items.Count);
        Assert.Equal((0, 199), Assert.Single(adapter.IndexRequests));
        Assert.Equal(1, first.NextSelector);
        Assert.Equal(199, first.NextIndexOffset);
        args.SelectorOffset = first.NextSelector!.Value;
        args.IndexOffset = first.NextIndexOffset;
        PropertyReadData last = Read(adapter, args);
        Assert.Equal(205, Assert.Single(last.Items).Index);
        Assert.Null(last.NextSelector);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_Exactly200Indexes_DoesNotInventAnEmptyRowOnContinuation()
    {
        var property = new FakeProperty { Indexes = [.. Enumerable.Range(6, 200)] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = Adapter(property);

        PropertyReadData first = Read(adapter);
        PropertyReadArgs args = Args();
        args.SelectorOffset = first.NextSelector!.Value;
        args.IndexOffset = first.NextIndexOffset;
        PropertyReadData final = Read(adapter, args);

        Assert.Equal(200, first.Items.Count);
        Assert.Empty(final.Items);
        Assert.Null(final.NextSelector);
        adapter.AssertDisposed();
    }
}
