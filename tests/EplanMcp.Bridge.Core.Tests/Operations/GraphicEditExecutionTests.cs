using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class GraphicEditExecutionTests
{
    public static TheoryData<string> Operations => GraphicEditPlannerTests.Operations;
    [Theory]
    [MemberData(nameof(Operations))]
    public void PreviewDoesNotStartTransaction(string operation)
    {
        var fixture = new Fixture(operation);
        string before = fixture.Data.ToString();
        JObject result = (JObject)new GraphicEditOperation(fixture, fixture, false).Execute(fixture.Input)!;
        Assert.NotEmpty((string)result["fingerprint"]!);
        Assert.Equal(0, fixture.Transactions);
        Assert.Equal(before, fixture.Data.ToString());
    }
    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryEditCommitsOnceAndReturnsReadback(string operation)
    {
        var fixture = new Fixture(operation);
        JObject result = fixture.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal((1, 1), (fixture.Transactions, fixture.Commits));
        Assert.Equal(1, fixture.Closed);
        Assert.NotNull(result["predicted"]);
    }
    [Theory]
    [MemberData(nameof(Operations))]
    public void OutOfScopeReadbackMismatchRollsBackBeforeCommit(string operation)
    {
        var fixture = new Fixture(operation) { DamageOutside = true };
        string before = fixture.Data.ToString();
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => fixture.Execute());
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.Equal(before, fixture.Data.ToString());
        Assert.Equal(0, fixture.Commits);
    }
    [Theory]
    [MemberData(nameof(Operations))]
    public void CommittedMismatchWarnsWithoutReplayingOrRollback(string operation)
    {
        var fixture = new Fixture(operation) { DamageAfterCommit = true };
        JObject result = fixture.Execute();
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Contains("Committed readback failed", (string)result["warning"]!, StringComparison.Ordinal);
        Assert.Equal((1, 0), (fixture.Commits, fixture.Rollbacks));
    }
    [Theory]
    [MemberData(nameof(Operations))]
    public void StaleExecutionNeverStartsATransaction(string operation)
    {
        var fixture = new Fixture(operation);
        string fingerprint = (string)GraphicEditPlanner.Plan(fixture.Input, fixture.Data)["fingerprint"]!;
        fixture.Data["objects"]![0]!["drawingOrder"] = 63;
        var execute = new GraphicEditExecuteOperation(fixture, fixture);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => execute.Execute(new JObject { ["request"] = fixture.Input, ["fingerprint"] = fingerprint })).Error.Reason);
        Assert.Equal(0, fixture.Transactions);
    }
    [Fact]
    public void AlreadySnappedDoesNotCreateUndoStep()
    {
        var fixture = new Fixture("snap_to_grid");
        fixture.Data["objects"]![0]!["location"] = new JArray(40, 40);
        JObject result = fixture.Execute();
        Assert.True((bool)result["noOp"]!);
        Assert.Equal(0, fixture.Transactions);
    }
    [Fact]
    public void ScalingCanChangeArcParametersWhilePreservingItsStyleAndIdentity()
    {
        var original = new JObject { ["kind"] = "Arc", ["properties"] = new JObject { ["arc_kind"] = "Circle", ["radius_mm"] = "10", ["start_angle"] = "45", ["pen_color"] = "1" } };
        var scaled = (JObject)original.DeepClone();
        scaled["properties"]!["arc_kind"] = "Ellipse";
        scaled["properties"]!["radius_mm"] = "20";
        scaled["properties"]!["start_angle"] = "26.5650511771";
        GraphicEditReadback.Stable(original, scaled, "scale", new JObject());
        scaled["properties"]!["pen_color"] = "2";
        Assert.Throws<BridgeErrorException>(() => GraphicEditReadback.Stable(original, scaled, "scale", new JObject()));
    }
    private sealed class Fixture : IGraphicEditor, ITransactionScopeFactory
    {
        public JObject Input { get; }
        public JObject Data { get; set; }
        public int Transactions, Commits, Rollbacks, Closed;
        public bool DamageOutside, DamageAfterCommit;
        public Fixture(string operation)
        {
            (Input, Data) = GraphicEditPlannerTests.Request(operation);
            if (operation == "snap_to_grid")
            { Data["objects"]![0]!["location"] = new JArray(40.5, 40.5); }
            JObject extra = (JObject)Data["objects"]![0]!.DeepClone();
            double dx = 800 - (double)extra["location"]![0]!, dy = 800 - (double)extra["location"]![1]!;
            extra["locator"]!["oid"] = 900;
            extra["location"] = new JArray(800, 800);
            extra["group"] = null;
            if (extra["bounds"] is JArray bounds)
            { extra["bounds"] = new JArray(bounds.Values<double>().Select((v, i) => v + (i % 2 == 0 ? dx : dy))); }
            if (extra["points"] is JArray points)
            { extra["points"] = new JArray(points.Select(p => new JArray((double)p[0]! + dx, (double)p[1]! + dy))); }
            ((JArray)Data["objects"]!).Add(extra);
        }
        public JObject Execute()
        {
            string hash = (string)GraphicEditPlanner.Plan(Input, Data)["fingerprint"]!;
            return (JObject)new GraphicEditExecuteOperation(this, this).Execute(new JObject { ["request"] = Input, ["fingerprint"] = hash })!;
        }
        public JObject Inspect(string project, string page)
        {
            if (DamageAfterCommit && Commits > 0)
            { Data["objects"]!.Single(r => (int)r["locator"]!["oid"]! == 900)["properties"]!["text"] = "unapproved"; }
            return (JObject)Data.DeepClone();
        }
        public Action PrepareEdit(JObject input, JObject before, JObject plan) => () =>
        {
            var objects = (JArray)Data["objects"]!;
            string op = (string)input["operation"]!;
            JToken? Find(JToken locator) => objects.SingleOrDefault(r => GraphicEditPlanner.Id(r["locator"]!) == GraphicEditPlanner.Id(locator));
            if (op is "cut_off" or "explode_block")
            {
                foreach (JToken locator in input["locators"]!)
                { Find(locator)!.Remove(); }
                if (op == "cut_off")
                { foreach (JToken row in plan["predicted"]!) { objects.Add(row.DeepClone()); } }
            }
            else
            {
                foreach (JToken expected in plan["predicted"]!)
                { Find(expected["locator"]!)!.Replace(expected.DeepClone()); }
                if (op == "order")
                { foreach (JToken locator in input["locators"]!) { Find(locator)!["drawingOrder"] = 65; } }
                if (op == "group")
                {
                    JObject group = (JObject)plan["predicted"]![0]!.DeepClone();
                    group["kind"] = "Group";
                    group["locator"]!["oid"] = 700;
                    group["members"] = input["locators"]!.DeepClone();
                    foreach (JToken locator in input["locators"]!)
                    { Find(locator)!["group"] = group["locator"]!.DeepClone(); }
                    objects.Add(group);
                }
                if (op == "ungroup")
                {
                    foreach (JToken expected in plan["predicted"]!)
                    { if ((string?)expected["kind"] == "Group") { Find(expected["locator"]!)!.Remove(); } else { Find(expected["locator"]!)!["group"] = null; } }
                }
            }
            foreach (JToken created in plan["created"]!)
            {
                JObject row = (JObject)plan["predicted"]![0]!.DeepClone();
                row["locator"]!["oid"] = 701;
                row["kind"] = (string?)created["kind"] == "arc" ? "Arc" : "Line";
                row["location"] = created["position"]!.DeepClone();
                row["points"] = new JArray(created["position"]!.DeepClone(), (created["endPosition"] ?? created["position"])!.DeepClone());
                if ((string?)created["kind"] == "arc")
                {
                    row["properties"]!["radius_mm"] = created["radiusMm"]!.ToString();
                    row["properties"]!["start_angle"] = created["startAngle"]!.ToString();
                    row["properties"]!["end_angle"] = created["endAngle"]!.ToString();
                }
                objects.Add(row);
            }
            if (DamageOutside)
            { objects.Single(r => (int)r["locator"]!["oid"]! == 900)["properties"]!["text"] = "unapproved"; }
        };
        public ITransactionScope Begin(string undoDescription)
        { Assert.Equal("AI: eplan_graphic_edit", undoDescription); Transactions++; return new Scope(this, (JObject)Data.DeepClone()); }
        private sealed class Scope(Fixture owner, JObject before) : ITransactionScope
        {
            public void Commit() => owner.Commits++;
            public void CloseUndo() => owner.Closed++;
            public void Rollback() { owner.Rollbacks++; owner.Data = before; }
            public void Dispose() { }
        }
    }
}
