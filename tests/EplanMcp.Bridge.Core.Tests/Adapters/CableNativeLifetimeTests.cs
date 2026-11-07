using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class CableNativeLifetimeTests
{
    [Theory]
    [InlineData(null, "0,75")]
    [InlineData("0.750", "0.750")]
    public void NumericSection_StoresCommaOnCoreAndPreservesExplicitCableText(string? text, string expectedText)
    {
        var store = new FakeCableStore();
        FakeCableStore.Current = store;
        store.Page.FindPlacements = store.FindPlacements;
        EplanProjects.Project = new(store.Page) { FindConnections = store.FindConnections };
        try
        {
            var adapter = new EplanConnectionCreator();
            JObject request = JObject.Parse("""
                {"items":[{"from":{"locator":{"type":17,"oid":1},"pin_index":0},
                            "to":{"locator":{"type":17,"oid":2},"pin_index":0}}]}
                """);
            JObject plan = JObject.Parse("""
                {"cableDefinitions":[{"designation":"-W1","points":[[32,16],[48,16]],
                  "type_text":"cable","core_count":1,"cross_section_mm2":0.75,
                  "cores":[{"label":"1","colour":"BK"}]}],
                 "coreImpact":[{"designation":"-W1","connection":{"item_index":0}}]}
                """);
            if (text is not null)
            {
                plan["cableDefinitions"]![0]!["cross_section_text"] = text;
            }
            new OperationContext("cable-section", true, null).Run(() =>
            {
                adapter.PrepareCables("project", "+A1/1", request, plan)();
                store.Wire.Cable = Assert.Single(store.Cables);
                adapter.BindCables("project", "+A1/1", request, plan);
                adapter.AssignCores("project", "+A1/1", request, plan);
                adapter.VerifyCables("project", "+A1/1", request, plan);
                Assert.Single(adapter.ReadCores("project", "+A1/1", request, plan));
                return true;
            });
            Assert.Equal(expectedText, Assert.Single(store.Cables).Properties[20043]);
            Assert.Equal("0,75", store.Wire.Properties[31002]);
            Assert.Equal(0, store.OpenLists);
        }
        finally
        {
            FakeCableStore.Current = null;
            EplanProjects.Project = null;
        }
    }

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
