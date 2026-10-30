using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class CableNativeLifetimeTests
{
    [Fact]
    public void ScopedFunctionList_DisposedBeforeCommit_ReleasesTheListAndInvalidatesItsValues()
    {
        var store = new FakeCableStore();
        var list = new FunctionPropertyList(store, []);
        list[(Properties.Function)20040] = "cable";
        PropertyValue value = list[(Properties.Function)20040];
        Assert.Equal("cable", value.ToString());
        Assert.Equal(1, store.OpenLists);

        list.Dispose();

        Assert.True(list.Disposed);
        Assert.Equal(0, store.OpenLists);
        Assert.Throws<ObjectDisposedException>(() => list[(Properties.Function)20040]);
        Assert.Throws<ObjectDisposedException>(() => value.IsEmpty);
        Assert.Throws<ObjectDisposedException>(list.Dispose);
    }

    [Fact]
    public void ScopeEnded_PreviouslyAcquiredConnectionAndList_RejectReadsAndEndpointChanges()
    {
        var store = new FakeCableStore();
        Connection connection = Assert.Single(store.FindConnections());
        ConnectionPropertyList list = connection.Properties;
        PropertyValue value = list[(Properties.Connection)31011];
        store.Generation++;

        Assert.Throws<ObjectDisposedException>(() => connection.Page);
        Assert.Throws<ObjectDisposedException>(() => connection.StartPin);
        Assert.Throws<ObjectDisposedException>(() => connection.EndPin);
        Assert.Throws<ObjectDisposedException>(() => connection.Properties);
        Assert.Throws<ObjectDisposedException>(() => connection.Page = null);
        Assert.Throws<ObjectDisposedException>(() => connection.StartPin = null);
        Assert.Throws<ObjectDisposedException>(() => connection.EndPin = null);
        Assert.Throws<ObjectDisposedException>(() => list[(Properties.Connection)31011]);
        Assert.Throws<ObjectDisposedException>(() => value.IsEmpty);
        Assert.Throws<ObjectDisposedException>(list.Dispose);
        Assert.Equal(1, store.OpenLists);
    }

    [Fact]
    public void ScopeEnded_PreviouslyAcquiredObjectListAndValue_ThrowOnReuse()
    {
        var store = new FakeCableStore();
#pragma warning disable CA2000 // A39/A55: the project owns this fake native cable; disposing it must fail.
        var cable = new Cable(store, new());
#pragma warning restore CA2000
        FunctionPropertyList list = cable.Properties;
        PropertyValue value = list[(Properties.Function)20040];
        store.Generation++;

        Assert.Throws<ObjectDisposedException>(() => cable.Properties);
        Assert.Throws<ObjectDisposedException>(() => list[(Properties.Function)20040]);
        Assert.Throws<ObjectDisposedException>(() => value.IsEmpty);
        Assert.Throws<ObjectDisposedException>(list.Dispose);
        Assert.Equal(1, store.OpenLists); // A55 forbids releasing a stale list after the scope ended.
    }
}
