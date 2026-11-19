using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementRenameTests
{
    private const string Page = "=GAA+A1/1";
    private const string Old = "=GAA-OLD";
    private const string New = "=GAA-XD4:2c";
    private readonly PlacementEditFixture _fake = new();

    public PlacementRenameTests()
    {
        _fake.Data["scene"]!["page"]!["page"] = Page;
        foreach (JToken row in _fake.Data["objects"]!)
        {
            row["locator"]!["page"] = Page;
            row["kind"] = "InterruptionPoint";
            row["root"] = true;
            row["name"] = Old;
            row["visibleName"] = "-OLD";
        }
        _fake.Counts = new(StringComparer.Ordinal) { [Old] = 3, [New] = 2 };
    }

    private JObject Request(params int[] indices)
    {
        JObject request = _fake.Input("rename", indices);
        request["page"] = Page;
        request["name"] = " -XD4:2c ";
        return request;
    }

    private JObject Plan(JObject request) =>
        (JObject)new PlacementEditOperation(_fake, _fake, false, _fake.Backup).Execute(request)!;

    private JObject Execute(JObject request, JObject plan) =>
        (JObject)new PlacementEditExecuteOperation(_fake, _fake, _fake.Backup).Execute(new JObject { ["request"] = request, ["fingerprint"] = plan["fingerprint"] })!;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RenamesSelectionAndReportsFullNamesAndPeers(int count)
    {
        JObject request = Request(Enumerable.Range(0, count).ToArray());
        JObject plan = Plan(request);
        Assert.All(plan["changes"]!, c => Assert.Equal((Old, New), ((string?)c["oldValue"], (string?)c["newValue"])));
        _fake.Mutation = () =>
        {
            foreach (JToken point in _fake.Data["objects"]!.Take(count))
            {
                point["name"] = New;
                point["visibleName"] = "display from EPLAN";
            }
        };
        JObject result = Execute(request, plan);
        Assert.Equal(count, result["predicted"]!.Count());
        Assert.All(result["predicted"]!, p =>
        {
            Assert.Equal((New, "display from EPLAN"), ((string?)p["name"], (string?)p["visibleName"]));
            Assert.Equal((2, 2), ((int)p["peersOld"]!, (int)p["peersNew"]!));
        });
        Assert.Equal((1, 1), (_fake.Transactions, _fake.Closed));
        Assert.Contains("eplan_connection_generate", (string?)result["hint"], StringComparison.Ordinal);
    }

    [Fact]
    public void SameFullNameSkipsWriteAndMixedSelectionListsOnlyChanges()
    {
        _fake.Data["objects"]![0]!["name"] = New;
        JObject request = Request(0);
        JObject result = Execute(request, Plan(request));
        Assert.True((bool)result["noOp"]!);
        Assert.Equal(1, (int)result["predicted"]![0]!["peersNew"]!);
        Assert.Equal(0, _fake.Transactions);
        Assert.Single(Plan(Request(0, 1))["changes"]!);
    }

    [Fact]
    public void RenameAcceptsStoredNamesWithoutStructureAndReportsOneWarning()
    {
        const string stored = "-XD4:2c";
        foreach (JToken point in _fake.Data["objects"]!)
        {
            point["name"] = "-OLD";
        }
        _fake.Counts = new(StringComparer.Ordinal)
        {
            ["-OLD"] = 3,
            [Old] = 1,
            ["=OTHER-OLD"] = 2,
            [stored] = 2,
            [New] = 3,
            ["=OTHER-XD4:2c"] = 4
        };
        JObject request = Request(0, 1);
        JObject plan = Plan(request);
        Assert.All(plan["changes"]!, c => Assert.Equal((Old, New), ((string?)c["oldValue"], (string?)c["newValue"])));
        _fake.Mutation = () =>
        {
            foreach (JToken point in _fake.Data["objects"]!.Take(2))
            {
                point["name"] = stored;
                point["visibleName"] = "XD4:2c";
            }
        };
        JObject result = Execute(request, plan);
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(new InterruptionNameReadback(New, stored).StructureWarning, (string?)result["warning"]);
        Assert.All(result["predicted"]!, p =>
        {
            Assert.Equal((stored, "XD4:2c"), ((string?)p["name"], (string?)p["visibleName"]));
            Assert.Equal((3, 5), ((int)p["peersOld"]!, (int)p["peersNew"]!));
        });
    }

    [Fact]
    public void StoredNameWithoutStructureIsAlreadyTheRequestedName()
    {
        const string stored = "-XD4:2c";
        _fake.Data["objects"]![0]!["name"] = stored;
        _fake.Counts = new(StringComparer.Ordinal) { [stored] = 4, [New] = 2 };
        JObject request = Request(0);
        JObject plan = Plan(request);
        Assert.Empty(plan["changes"]!);
        JObject preview = JObject.Parse((string)plan["details"]!["preview"]!);
        Assert.Equal(stored, (string?)preview["predicted"]![0]!["name"]);
        Assert.Equal(new InterruptionNameReadback(New, stored).StructureWarning, (string?)preview["warning"]);
        JObject result = Execute(request, plan);
        Assert.True((bool)result["noOp"]!);
        JToken row = Assert.Single(result["predicted"]!);
        Assert.True((bool)row["noOp"]!);
        Assert.Equal(stored, (string?)row["name"]);
        Assert.Equal((5, 5), ((int)row["peersOld"]!, (int)row["peersNew"]!));
        Assert.Equal(0, _fake.Transactions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\tb")]
    [InlineData("a\n")]
    [InlineData(null)]
    public void InvalidNameIsRejected(string? name)
    {
        JObject request = Request();
        request["name"] = name;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(request)).Error.Reason);
        request["name"] = new string('x', 129);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(request)).Error.Reason);
        Assert.Equal(0, _fake.Transactions);
    }

    [Fact]
    public void WrongKindNamesTheOffendingPlacement()
    {
        _fake.Data["objects"]![1]!["kind"] = "Text";
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Plan(Request(0, 1)));
        Assert.Contains("Text", ex.Error.Metadata!["violations"], StringComparison.Ordinal);
        _fake.Data["objects"]![1]!["kind"] = "InterruptionPoint";
        _fake.Data["objects"]![1]!["root"] = false;
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(Request(0, 1))).Error.Reason);
        Assert.Equal(0, _fake.Transactions);
    }

    [Fact]
    public void UnknownCountsAllowPlanAndWrongStoredNameRollsBack()
    {
        _fake.Counts = null;
        JObject request = Request();
        JObject plan = Plan(request);
        JObject preview = JObject.Parse((string)plan["details"]!["preview"]!);
        Assert.Contains("counts", (string?)preview["warning"], StringComparison.Ordinal);
        Assert.Equal(JTokenType.Null, preview["predicted"]![0]!["peersOld"]!.Type);
        Assert.Equal(JTokenType.Null, preview["predicted"]![0]!["peersNew"]!.Type);
        _fake.Mutation = () => _fake.Data["objects"]![0]!["name"] = "-XD4:3c";
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(request, plan)).Error.Execution);
        Assert.Equal(Old, (string?)_fake.Data["objects"]![0]!["name"]);
    }
}
