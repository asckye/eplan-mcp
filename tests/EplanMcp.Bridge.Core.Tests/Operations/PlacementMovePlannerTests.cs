using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementMovePlannerTests
{
    [Fact]
    public void ParentMove_IncludesEveryContextPropertyWithSharedIdentifier()
    {
        var f = new MoveFixture();
        f.Child('b', [22, 22]);
        f.Child('c', [24, 24]);
        JObject result = f.Plan();
        Assert.Equal(3, ((JArray)result["moves"]!).Count);
        Assert.Equal([25, 25], result["moves"]![0]!["after"]!.Values<double>());
        Assert.Equal([27, 27], result["moves"]![1]!["after"]!.Values<double>());
        Assert.Equal([29, 29], result["moves"]![2]!["after"]!.Values<double>());
        Assert.Equal(3, result["moves"]!.Select(r => (string?)r["locator"]!["digest"]).Distinct().Count());
        Assert.All(f.Scene.Page.Placements, p => Assert.Equal(20, p.Parent is null ? p.Location[0] : p.Parent.Oid == 1 ? 20 : 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertyText_IndependentOrAlongsideParent_IsRejected(bool includeParent)
    {
        var f = new MoveFixture();
        DrawingPlacement child = f.Child('b', [22, 22]);
        JObject move = MoveFixture.Move(child.Locator, [1, 1]);
        f.Request["moves"] = includeParent ? new JArray(f.Request["moves"]![0]!.DeepClone(), move) : new JArray(move);
        AssertError(f, ErrorReason.InvalidArgument, "/moves/");
    }

    [Theory]
    [InlineData("Function", "true")]
    [InlineData("Text", "true")]
    [InlineData("Group", "false")]
    [InlineData("MacroBox", "false")]
    [InlineData("Function", null)]
    public void GroupMacroOrUnknownMembership_IsRefused(string kind, string? grouped)
    {
        var f = new MoveFixture();
        f.Root.Kind = kind;
        if (grouped is null)
        { f.Root.Properties.Clear(); }
        else
        { f.Root.Properties["grouped"] = grouped; }
        AssertError(f, ErrorReason.CapabilityMissing);
    }

    [Fact]
    public void GroupedPropertyChild_IsRefusedEvenWhenParentIsPlain()
    {
        var f = new MoveFixture();
        f.Child('b', [22, 22]).Properties["grouped"] = "true";
        AssertError(f, ErrorReason.CapabilityMissing);
    }

    [Fact]
    public void DuplicateRoot_IsRejected()
    {
        var f = new MoveFixture();
        ((JArray)f.Request["moves"]!).Add(f.Request["moves"]![0]!.DeepClone());
        AssertError(f, ErrorReason.InvalidArgument, "/moves/1/locator");
    }

    [Theory]
    [InlineData(true, 25)]
    [InlineData(false, 22.5)]
    public void Delta_ResolvesAbsoluteBeforeSnapping(bool snap, double expected)
    {
        var f = new MoveFixture();
        f.Request["snap_to_grid"] = snap;
        f.Request["moves"]![0]!["delta_mm"] = new JArray(2.5, 2.5);
        Assert.Equal([expected, expected], f.Plan()["moves"]![0]!["after"]!.Values<double>());
    }

    [Fact]
    public void AbsoluteTarget_EqualsDeltaPlanGeometry()
    {
        var f = new MoveFixture();
        JObject delta = f.Plan();
        f.Request["moves"] = new JArray(new JObject { ["locator"] = BridgeJson.FromObject(f.Root.Locator), ["x_mm"] = 25, ["y_mm"] = 25 });
        Assert.True(JToken.DeepEquals(delta["moves"], f.Plan()["moves"]));
    }

    [Theory]
    [InlineData("null", "/moves/0")]
    [InlineData("{\"locator\":{},\"delta_mm\":[0,0]}", "/locator/project")]
    [InlineData("{\"locator\":{},\"delta_mm\":[0,0],\"x_mm\":1}", "/x_mm")]
    public void InvalidShape_HasPointer(string move, string path)
    {
        var f = new MoveFixture();
        f.Request["moves"] = new JArray(JToken.Parse(move));
        AssertError(f, ErrorReason.InvalidArgument, path);
    }

    [Fact]
    public void StrictNumbersKeysAndLocatorLimits_DoNotThrowUnmappedExceptions()
    {
        foreach (string mutation in new[] { "unknown", "extra/key", "hugeOid", "hugeType", "digest", "delta", "overflow", "duplicate", "empty" })
        {
            var f = new MoveFixture();
            switch (mutation)
            {
                case "unknown":
                    f.Request["unknown"] = true;
                    break;
                case "extra/key":
                    f.Request["moves"]![0]!["extra/key"] = true;
                    break;
                case "hugeOid":
                    f.Request["moves"]![0]!["locator"]!["oid"] = JToken.Parse("9223372036854775808");
                    break;
                case "hugeType":
                    f.Request["moves"]![0]!["locator"]!["type"] = 2147483648;
                    break;
                case "digest":
                    f.Request["moves"]![0]!["locator"]!["digest"] = new string('A', 64);
                    break;
                case "delta":
                    f.Request["moves"]![0]!["delta_mm"] = new JArray(0);
                    break;
                case "overflow":
                    f.Request["moves"]![0]!["delta_mm"] = JArray.Parse("[1e400,0]");
                    break;
                case "empty":
                    f.Request["moves"] = new JArray();
                    break;
                case "duplicate":
                    BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => PlacementMovePlanner.Plan(f.Request.ToString().Replace("\"dry_run\": true", "\"dry_run\": true, \"dry_run\": true", StringComparison.Ordinal), f.Scene));
                    Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
                    continue;
            }
            AssertError(f, ErrorReason.InvalidArgument, mutation == "extra/key" ? "/extra~1key" : null);
        }
    }

    [Fact]
    public void ClosureBoundsAndCollision_FailClosed()
    {
        var f = new MoveFixture();
        f.Child('b', [98, 22]);
        AssertError(f, ErrorReason.InvalidArgument, "/moves");
        f = new MoveFixture();
        f.Child('b', [22, 22]).Bounds = null;
        AssertError(f, ErrorReason.CapabilityMissing);
        f = new MoveFixture();
        f.Scene.Page.Placements.Add(MoveFixture.Placement(2, 'd', [25, 25]));
        AssertError(f, ErrorReason.InvalidArgument, "/moves");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.Epsilon)]
    public void InvalidGrid_IsRejected(double grid)
    {
        var f = new MoveFixture();
        f.Scene.Page.GridSize = grid;
        AssertError(f, ErrorReason.InvalidArgument);
    }

    [Fact]
    public void SymbolMove_PredictsPulledAutoconnectAndAffectedLogicalConnection()
    {
        var f = new MoveFixture();
        f.Connect();
        f.Request["moves"]![0]!["delta_mm"] = new JArray(5, 0);
        JObject result = f.Plan();
        JToken graphical = Assert.Single(result["graphicalChanges"]!);
        Assert.Equal(["17/1/0", "17/2/0"], graphical["pins"]!.Values<string>());
        Assert.Equal([20, 20], graphical["before"]![0]!.Values<double>());
        Assert.Equal([25, 20], graphical["after"]![0]!.Values<double>());
        Assert.Equal(["17/1/0", "17/2/0"], Assert.Single(result["connectionChanges"]!).Values<string>());
        Assert.Equal([20, 20], f.Scene.Pins["17/1/0"].Location);
        f.Request["moves"]![0]!["delta_mm"] = new JArray(5, 5);
        Assert.Equal(JTokenType.Null, f.Plan()["graphicalChanges"]![0]!["after"]!.Type);
    }

    [Fact]
    public void NewAlignment_PredictsNewAutoconnect()
    {
        var f = new MoveFixture();
        f.Connect();
        f.Scene.Pins["17/2/0"].Location = [40, 25];
        f.Scene.Graphical.Clear();
        JObject result = f.Plan();
        Assert.Equal(JTokenType.Null, result["graphicalChanges"]![0]!["before"]!.Type);
        Assert.Equal([25, 25], result["graphicalChanges"]![0]!["after"]![0]!.Values<double>());
    }

    [Fact]
    public void MovingTextOntoExistingAutoconnect_IsAConflict()
    {
        var f = new MoveFixture();
        f.Root.Kind = "Text";
        f.Root.Location = [20, 40];
        f.Root.Bounds = [19, 39, 21, 41];
        f.Scene.Page.Placements.Add(MoveFixture.Placement(2, 'd', [20, 20]));
        f.Scene.Page.Placements.Add(MoveFixture.Placement(3, 'e', [40, 20]));
        f.Scene.Pins.Add("17/2/0", new() { Location = [20, 20], Direction = "Right" });
        f.Scene.Pins.Add("17/3/0", new() { Location = [40, 20], Direction = "Left" });
        f.Scene.Graphical.Add([[20, 20], [40, 20]]);
        f.Request["moves"]![0]!["delta_mm"] = new JArray(10, -20);
        AssertError(f, ErrorReason.InvalidArgument, "/moves");
    }

    [Fact]
    public void ChangedAutoconnect_RejectsStationaryOutlineAndKeepsUnrelatedConnectionsOut()
    {
        var f = new MoveFixture();
        f.Connect();
        f.Request["moves"]![0]!["delta_mm"] = new JArray(5, 0);
        f.Scene.Page.Placements.Add(MoveFixture.Placement(3, 'e', [20, 60]));
        f.Scene.Page.Placements.Add(MoveFixture.Placement(4, 'f', [40, 60]));
        f.Scene.Pins.Add("17/3/0", new() { Location = [20, 60], Direction = "Right" });
        f.Scene.Pins.Add("17/4/0", new() { Location = [40, 60], Direction = "Left" });
        f.Scene.Graphical.Add([[20, 60], [40, 60]]);
        f.Scene.Logical.Add(new() { StartDevice = "17/3/0", EndDevice = "17/4/0" });
        Assert.Single(f.Plan()["connectionChanges"]!);
        f.Scene.Page.Placements.Add(MoveFixture.Placement(9, 'e', [30, 20]));
        AssertError(f, ErrorReason.InvalidArgument, "/moves");
    }

    [Fact]
    public void IncompleteOrUnpredictableEvidence_IsRefused()
    {
        var f = new MoveFixture();
        f.Scene.Complete = false;
        AssertError(f, ErrorReason.CapabilityMissing);
        f.Scene.Complete = true;
        f.Scene.Page.PageSize = null;
        f.Scene.Page.PlotFrameSize = [100, 100];
        AssertError(f, ErrorReason.CapabilityMissing);
        f = new MoveFixture();
        f.Connect();
        f.Scene.Graphical.Clear();
        AssertError(f, ErrorReason.CapabilityMissing);
    }

    [Fact]
    public void ZeroDelta_OffGridPositionStaysUnchanged()
    {
        var f = new MoveFixture();
        f.Root.Location = [22.5, 22.5];
        f.Request["moves"]![0]!["delta_mm"] = new JArray(0, 0);
        JObject result = f.Plan();
        Assert.True((bool)result["noOp"]!);
        Assert.Equal([22.5, 22.5], result["moves"]![0]!["after"]!.Values<double>());
        Assert.False((bool)result["moves"]![0]!["snapped"]!);
    }

    [Fact]
    public void ExecutionRequest_ZeroDelta_IsNoOp()
    {
        var f = new MoveFixture();
        f.Request["dry_run"] = false;
        f.Request["moves"]![0]!["delta_mm"] = new JArray(0, 0);
        Assert.True((bool)f.Plan()["noOp"]!);
    }

    internal static void AssertError(MoveFixture f, string reason, string? pointer = null)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => f.Plan()).Error;
        Assert.Equal(reason, error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        if (pointer is not null)
        { Assert.Contains(pointer, error.Metadata!["violations"], StringComparison.Ordinal); }
    }
}

internal sealed class MoveFixture
{
    internal ConnectionCreatePlanner.Scene Scene { get; } = new()
    {
        Complete = true,
        Project = new() { Name = "Line1", Path = "scratch-project" },
        Page = new() { Project = "scratch-project", Page = "P", GridSize = 5, PageSize = [100, 100] },
    };
    internal DrawingPlacement Root { get; } = Placement(1, 'a', [20, 20]);
    internal JObject Request { get; }
    internal MoveFixture()
    {
        Scene.Page.Placements.Add(Root);
        Request = new() { ["project"] = "Line1", ["page"] = "P", ["dry_run"] = true, ["moves"] = new JArray(Move(Root.Locator, [5, 5])) };
    }
    internal DrawingPlacement Child(char digest, double[] location)
    {
        DrawingPlacement child = Placement(1, digest, location);
        child.Kind = "PropertyPlacement";
        child.Parent = Root.Locator;
        Scene.Page.Placements.Add(child);
        return child;
    }
    internal void Connect()
    {
        Scene.Page.Placements.Add(Placement(2, 'd', [40, 20]));
        Scene.Pins.Add("17/1/0", new() { Location = [20, 20], Direction = "Right" });
        Scene.Pins.Add("17/2/0", new() { Location = [40, 20], Direction = "Left" });
        Scene.Graphical.Add([[20, 20], [40, 20]]);
        Scene.Logical.Add(new() { StartDevice = "17/1/0", EndDevice = "17/2/0" });
    }
    internal static DrawingPlacement Placement(long oid, char digest, double[] location) => new()
    {
        Locator = new() { Project = "scratch-project", Page = "P", Type = 17, Oid = oid, Digest = new string(digest, 64) },
        Kind = "Function",
        Location = location,
        Bounds = [location[0] - 1, location[1] - 1, location[0] + 1, location[1] + 1],
        Properties = new() { ["grouped"] = "false" },
    };
    internal static JObject Move(DrawingLocator locator, double[] delta) => new() { ["locator"] = BridgeJson.FromObject(locator), ["delta_mm"] = new JArray(delta) };
    internal JObject Plan(string? fingerprint = null) => PlacementMovePlanner.Plan(Request.ToString(), Scene, fingerprint);
}
