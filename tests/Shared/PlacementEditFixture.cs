using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Tests;

internal sealed class PlacementEditFixture : IPlacementEditor, ITransactionScopeFactory
{
    public JObject Data { get; set; }
    public Action? Mutation { get; set; }
    public bool FailRollback { get; set; }
    public int Transactions { get; private set; }
    public int Closed { get; private set; }
    public int Backups { get; private set; }
    public PlacementEditFixture(string path = "project")
    {
        var scene = new ConnectionCreatePlanner.Scene
        {
            Complete = true,
            Project = new() { Name = "Line1", Path = path },
            Page = new() { Project = path, Page = "P", GridSize = 1, PageSize = [400, 300] }
        };
        foreach ((int oid, double x, double y) in new[] { (1, 20d, 20d), (2, 50d, 60d), (3, 120d, 100d) })
        {
            scene.Page.Placements.Add(new()
            {
                Kind = "Text",
                Location = [x, y],
                Bounds = [x - 1, y - 1, x + 1, y + 1],
                Locator = new() { Project = path, Page = "P", Type = 4, Oid = oid, Digest = new string('a', 64) },
                Properties = new() { ["grouped"] = "false", ["text"] = "??_??@note;" }
            });
        }
        Data = new()
        {
            ["scene"] = BridgeJson.FromObject(scene),
            ["functions"] = new JArray(),
            ["connections"] = new JArray(),
            ["objects"] = new JArray(scene.Page.Placements.Select(row =>
            {
                var obj = (JObject)BridgeJson.FromObject(row);
                obj.Remove("parent");
                obj["drawing"] = obj["locator"]!.DeepClone();
                obj["angle"] = 0d;
                obj["children"] = new JArray();
                obj["pins"] = new JObject();
                return obj;
            }))
        };
    }
    public JObject Input(string op = "rotate", params int[] indices) => new()
    {
        ["project"] = "Line1",
        ["page"] = "P",
        ["operation"] = op,
        ["locators"] = new JArray((indices.Length == 0 ? [0] : indices).Select(i => Data["objects"]![i]!["locator"]!.DeepClone()))
    };
    public JObject Inspect(string project, string page) => (JObject)Data.DeepClone();
    public ConnectionCreatePlanner.Scene Read(string project, string page) => Data["scene"]!.ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
    public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
    {
        foreach (JToken move in moves)
        {
            JToken root = Data["objects"]!.Single(r => PlacementEditPlanner.Id(r["locator"]!) == PlacementEditPlanner.Id(move["locator"]!));
            double dx = (double)move["after"]![0]! - (double)root["location"]![0]!, dy = (double)move["after"]![1]! - (double)root["location"]![1]!;
            root["location"] = move["after"]!.DeepClone();
            root["bounds"] = move["bounds"]!.DeepClone();
            JToken row = Data["scene"]!["page"]!["placements"]!.Single(r => PlacementEditPlanner.Id(r["locator"]!) == PlacementEditPlanner.Id(move["locator"]!));
            row["location"] = new JArray((double)row["location"]![0]! + dx, (double)row["location"]![1]! + dy);
            row["bounds"] = root["bounds"]!.DeepClone();
        }
    };
    public Action PrepareEdit(JObject input, JObject before) => () => Mutation?.Invoke();
    public ITransactionScope Begin(string undoDescription)
    {
        Assert.Equal("AI: eplan_placement_edit", undoDescription);
        Transactions++;
        return new Scope(this, (JObject)Data.DeepClone());
    }
    public void Backup(OpenProject project, BackupTarget? target)
    {
        Assert.NotNull(target);
        Assert.Equal(project.Path, (string?)Data["scene"]!["project"]!["path"]);
        Backups++;
    }
    private sealed class Scope(PlacementEditFixture owner, JObject before) : ITransactionScope
    {
        public void Commit() { }
        public void CloseUndo() => owner.Closed++;
        public void Rollback() { if (owner.FailRollback) { throw new InvalidOperationException("Injected rollback failure."); } owner.Data = (JObject)before.DeepClone(); }
        public void Dispose() { }
    }
}
