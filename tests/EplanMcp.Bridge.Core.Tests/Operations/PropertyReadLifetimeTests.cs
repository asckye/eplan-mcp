using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyReadLifetimeTests
{
    public static TheoryData<string> Stages => new()
    {
        "scope.opened", "property.lookup", "property.opened", "definition.opened", "metadata", "value.lookup",
        "value.opened", "default_language", "is_empty", "texts.opened", "languages", "text", "display",
    };

    [Theory]
    [MemberData(nameof(Stages))]
    public void Read_ExceptionAtEveryAcquisitionOrGetter_DisposesEveryAcquiredResourceOnce(string stage)
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(stage == "display" ? new FakeProperty() : PropertyReadTextTests.Multilingual());
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                throw new InvalidOperationException("injected failure");
            }
        };
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Detailed = true;

        Assert.Throws<InvalidOperationException>(() => PropertyReadOperationTests.Read(adapter, args));

        Assert.Contains(stage, adapter.Events);
        adapter.AssertDisposed();
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Read_CancellationAtEveryAcquisitionOrGetter_DisposesEveryAcquiredResourceOnce(string stage)
    {
        var context = new OperationContext("read", write: false, progress: null);
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(stage == "display" ? new FakeProperty() : PropertyReadTextTests.Multilingual());
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                context.Cancel();
            }
        };
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Detailed = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => PropertyReadOperationTests.Read(adapter, args)));

        Assert.Equal(ErrorReason.Cancelled, error.Error.Reason);
        Assert.Null(error.Error.Execution);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("indexes")]
    [InlineData("stored")]
    public void Read_ExceptionAtIndexesOrTypedGetter_DisposesEveryAcquiredResourceOnce(string stage)
    {
        var property = new FakeProperty { Indexes = [5] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                throw new InvalidOperationException("injected");
            }
        };

        Assert.Throws<InvalidOperationException>(() => PropertyReadOperationTests.Read(adapter));

        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_PreCancelled_DoesNotOpenTheObject()
    {
        var context = new OperationContext("read", write: false, progress: null);
        context.Cancel();
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter();

        Assert.Throws<BridgeErrorException>(() => context.Run(() => PropertyReadOperationTests.Read(adapter)));

        Assert.Equal(0, adapter.Opens);
    }

    [Fact]
    public void Read_SoftBudgetAtScalarBoundary_ReturnsContinuationAndDisposesList()
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter();
        adapter.Properties.Add(10014, new FakeProperty());
        TimeSpan elapsed = TimeSpan.Zero;
        adapter.OnAccess = at =>
        {
            if (at == "stored")
            {
                elapsed = TimeSpan.FromSeconds(2);
            }
        };

        PropertyReadData data = PropertyReadOperationTests.Read(adapter, PropertyReadOperationTests.Args(10013, 10014), () => elapsed);

        Assert.Single(data.Items);
        Assert.Equal(1, data.NextSelector);
        Assert.Equal(0, data.NextIndexOffset);
        Assert.Single(adapter.ValueRequests);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_SoftBudgetAtIndexBoundary_ResumesAtNextExistingIndex()
    {
        var property = new FakeProperty { Indexes = [-7, 13, 204] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        TimeSpan elapsed = TimeSpan.Zero;
        adapter.OnAccess = at =>
        {
            if (at == "stored")
            {
                elapsed = TimeSpan.FromSeconds(2);
            }
        };

        PropertyReadData data = PropertyReadOperationTests.Read(adapter, elapsed: () => elapsed);

        Assert.Equal(-7, Assert.Single(data.Items).Index);
        Assert.Equal(0, data.NextSelector);
        Assert.Equal(1, data.NextIndexOffset);
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.IndexOffset = data.NextIndexOffset;
        adapter.OnAccess = null;
        PropertyReadData remaining = PropertyReadOperationTests.Read(adapter, args);
        Assert.Equal(new int?[] { 13, 204 }, remaining.Items.Select(r => r.Index));
        Assert.Null(remaining.NextSelector);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("scope.opened")]
    [InlineData("definition.opened")]
    [InlineData("is_empty")]
    [InlineData("text")]
    [InlineData("display")]
    public void Read_HardBudgetExceededAfterNativeWork_FailsAtCheckpointAndDisposes(string stage)
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(stage == "display" ? new FakeProperty() : PropertyReadTextTests.Multilingual());
        TimeSpan elapsed = TimeSpan.Zero;
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                elapsed = TimeSpan.FromSeconds(20);
            }
        };
        PropertyReadArgs args = PropertyReadOperationTests.Args();
        args.Detailed = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PropertyReadOperationTests.Read(adapter, args, () => elapsed));

        Assert.Equal(ErrorReason.Timeout, error.Error.Reason);
        adapter.AssertDisposed();
    }

    [Fact]
    public void Read_MultipleSelectors_AcquiresOneListAndReleasesItAfterEveryChild()
    {
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter();
        adapter.Properties.Add(10014, new FakeProperty());

        Assert.Equal(2, PropertyReadOperationTests.Read(adapter, PropertyReadOperationTests.Args(10013, 10014)).Items.Count);

        Assert.Equal(1, adapter.Opens);
        Assert.Equal("disposed.list", adapter.Events[^1]);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("indexes")]
    [InlineData("stored")]
    public void Read_CancelAtIndexesOrTypedGetter_StopsAndDisposes(string stage)
    {
        var context = new OperationContext("read", write: false, progress: null);
        var property = new FakeProperty { Indexes = [5, 8] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                context.Cancel();
            }
        };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => context.Run(() => PropertyReadOperationTests.Read(adapter)));

        Assert.Equal(ErrorReason.Cancelled, error.Error.Reason);
        Assert.InRange(adapter.ValueRequests.Count, 0, 1);
        adapter.AssertDisposed();
    }

    [Theory]
    [InlineData("scope.opened")]
    [InlineData("indexes")]
    public void Read_SoftBudgetBeforeNextValue_ReturnsWithoutStartingAnotherGetter(string stage)
    {
        var property = new FakeProperty { Indexes = [5, 8] };
        property.Metadata.IsIndexed = true;
        FakePropertyReadAdapter adapter = PropertyReadOperationTests.Adapter(property);
        TimeSpan elapsed = TimeSpan.Zero;
        adapter.OnAccess = at =>
        {
            if (at == stage)
            {
                elapsed = TimeSpan.FromSeconds(2);
            }
        };

        PropertyReadData data = PropertyReadOperationTests.Read(adapter, elapsed: () => elapsed);

        Assert.Empty(data.Items);
        Assert.Empty(adapter.ValueRequests);
        Assert.Equal(0, data.NextSelector);
        Assert.Equal(0, data.NextIndexOffset);
        adapter.AssertDisposed();
    }
}
