using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class CableNativeLifetimeTests
{
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
