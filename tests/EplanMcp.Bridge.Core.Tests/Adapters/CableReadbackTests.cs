using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Medium")]
public sealed class CableReadbackTests
{
    [Fact]
    public void CommittedStages_ReadbackRediscoversNativeObjectsAndReleasesFreshLists()
    {
        using var scopes = new Scopes();
        var adapter = new EplanConnectionCreator();
        JObject request = JObject.Parse("""
            {"items":[{"from":{"locator":{"type":17,"oid":1},"pin_index":0},
                        "to":{"locator":{"type":17,"oid":2},"pin_index":0}}]}
            """);
        JObject plan = JObject.Parse("""
            {"cableDefinitions":[{"designation":"-W1","points":[[32,16],[48,16]],
              "type_text":"cable","core_count":1,"cross_section_mm2":1.5,
              "cores":[{"label":"1","colour":"BK"}]}],
             "coreImpact":[{"designation":"-W1","connection":{"item_index":0}}]}
            """);
        new OperationContext("cable-lifetime", true, null).Run(() =>
        {
            Action create = adapter.PrepareCables("project", "+A1/1", request, plan);
            scopes.Commit(() => { }); // Earlier symbol/generation stages must not leave handles in the closure.
            scopes.Commit(create);
            Read(() => adapter.VerifyCables("project", "+A1/1", request, plan));
            scopes.Commit(() => scopes.Store.Wire.Cable = Assert.Single(scopes.Store.Cables));
            scopes.Commit(() => adapter.BindCables("project", "+A1/1", request, plan));
            Read(() => adapter.VerifyBindings("project", "+A1/1", request, plan));
            scopes.Commit(() => { });
            scopes.Commit(() => adapter.AssignCores("project", "+A1/1", request, plan));
            ReadCores();
            scopes.Commit(() => { });
            ReadCores();
            return true;
        });
        Assert.Equal(7, File.ReadAllLines(scopes.Journal).Length);

        void Read(Action read)
        {
            int enumerations = scopes.Store.PlacementEnumerations, lists = scopes.Store.ListsAcquired;
            read();
            Assert.True(scopes.Store.PlacementEnumerations > enumerations);
            Assert.True(scopes.Store.ListsAcquired > lists);
            Assert.Equal(0, scopes.Store.OpenLists);
        }
        void ReadCores() => Read(() =>
        {
            JToken row = Assert.Single(adapter.ReadCores("project", "+A1/1", request, plan));
            Assert.Equal("-W1", (string?)row["designation"]);
            Assert.Equal("cable", (string?)row["type"]);
            Assert.Equal("1", (string?)row["label"]);
            Assert.Equal("BK", (string?)row["colour"]);
        });
    }

    private sealed class Scopes : ITransactionScopeFactory, IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("cable-lifetime-").FullName;
        public FakeCableStore Store { get; } = new();
        public string Journal => Path.Combine(_root, "commits");
        public Scopes()
        {
            FakeCableStore.Current = Store;
            Store.Page.FindPlacements = Store.FindPlacements;
            EplanProjects.Project = new(Store.Page) { FindConnections = Store.FindConnections };
        }
        public void Commit(Action change) => TransactionRunner.Run(this, "cable-lifetime", () => { change(); return true; }, _ => true);
        public ITransactionScope Begin(string undoDescription) => new Scope(this);
        private sealed class Scope(Scopes owner) : ITransactionScope
        {
            public void Commit()
            {
                Assert.Equal(0, owner.Store.OpenLists);
                File.AppendAllLines(owner.Journal, ["committed"]);
                owner.Store.Generation++;
            }
            public void Rollback() { Assert.Equal(0, owner.Store.OpenLists); owner.Store.Generation++; }
            public void CloseUndo() { }
            public void Dispose() => owner.Store.Generation++;
        }
        public void Dispose()
        {
            FakeCableStore.Current = null;
            EplanProjects.Project = null;
            Directory.Delete(_root, true);
        }
    }
}
