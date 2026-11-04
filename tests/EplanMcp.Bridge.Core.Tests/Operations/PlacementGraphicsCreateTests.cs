using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementGraphicsCreateTests
{
    [Theory]
    [InlineData("polyline", "points")]
    [InlineData("arc", "radiusMm")]
    [InlineData("arc", "startAngle")]
    [InlineData("arc", "endAngle")]
    [InlineData("path_text", "contents")]
    [InlineData("path_text", "heightMm")]
    [InlineData("image", "file")]
    [InlineData("image", "filePath")]
    [InlineData("image", "sha256")]
    public void MissingRequiredMember_RejectsBeforeWriting(string kind, string member)
    {
        var store = new PlacementTextStore();
        JObject request = (JObject)BridgeJson.FromObject(Request(kind));
        ((JObject)request["items"]![0]!).Remove(member);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => store.Operation.ParseArgs(request)).Error.Reason);
        Assert.Empty(store.Transactions.Calls);
    }

    [Theory]
    [InlineData("polyline", "empty")]
    [InlineData("polyline", "one")]
    [InlineData("polyline", "invalid")]
    [InlineData("arc", "zero")]
    [InlineData("arc", "negative")]
    [InlineData("arc", "infinity")]
    [InlineData("arc", "nan")]
    [InlineData("path_text", "height")]
    [InlineData("path_text", "contents")]
    [InlineData("image", "extension")]
    public void InvalidGeometryOrImage_RejectsBeforeWriting(string kind, string problem)
    {
        var store = new PlacementTextStore();
        PlacementTextCreateArgs args = Request(kind);
        PlacementText item = args.Items![0];
        switch (problem)
        {
            case "empty":
                item.Points = [];
                break;
            case "one":
                item.Points = [[20, 20]];
                break;
            case "invalid":
                item.Points[1] = [double.NaN, 20];
                break;
            case "zero":
                item.RadiusMm = 0;
                break;
            case "negative":
                item.RadiusMm = -1;
                break;
            case "infinity":
                item.RadiusMm = double.PositiveInfinity;
                break;
            case "nan":
                item.EndAngle = double.NaN;
                break;
            case "height":
                item.HeightMm = 0;
                break;
            case "contents":
                item.Contents.Clear();
                break;
            case "extension":
                item.FilePath = Path.GetFullPath("image.exe");
                break;
        }
        BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error;
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(store.Transactions.Calls);
    }

    [Theory]
    [InlineData("path_text", true)]
    [InlineData("text", false)]
    public void DefaultHeight_IsAcceptedOnlyForPathText(string kind, bool valid)
    {
        var store = new PlacementTextStore();
        PlacementTextCreateArgs request = Request("path_text");
        request.Items![0].Kind = kind;
        request.Items[0].HeightMm = -16002;
        if (valid)
        {
            var result = Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(request));
            Assert.Equal(-16002, result.Items[0].HeightMm);
        }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(request)).Error.Reason); }
    }

    [Fact]
    public void DefaultHeight_ReadbackMustPreserveSentinel()
    {
        var store = new PlacementTextStore { Readback = item => { item.HeightMm = 3.5; return item; } };
        PlacementTextCreateArgs request = Request("path_text");
        request.Items![0].HeightMm = -16002;
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(request)).Error.Reason);
        Assert.Empty(store.Texts);
    }

    [Theory]
    [InlineData("polyline")]
    [InlineData("arc")]
    [InlineData("path_text")]
    public void Geometry_PreservesAdapterRequestAndTransactionShape(string kind)
    {
        var store = new PlacementTextStore();
        PlacementTextCreateArgs args = Request(kind);
        var parsed = Assert.IsType<PlacementTextCreateArgs>(store.Operation.ParseArgs(BridgeJson.FromObject(args)));
        var result = Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(parsed));
        Assert.True(JToken.DeepEquals(BridgeJson.FromObject(args.Items![0]), BridgeJson.FromObject(result.Items[0])));
        Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], store.Transactions.Calls);
    }

    [Theory]
    [InlineData("polyline")]
    [InlineData("arc")]
    [InlineData("path_text")]
    public void WrongAdapterReadback_RollsBackBatch(string kind)
    {
        var store = new PlacementTextStore();
        store.Readback = item =>
        {
            if (kind == "polyline")
            { item.Points[1][0] += 1; }
            else if (kind == "arc")
            { item.StartAngle += 1; }
            else
            { item.Contents["??_??"] = "changed"; }
            return item;
        };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(Request(kind))).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(store.Texts);
        Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], store.Transactions.Calls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonFiniteArcReadback_RollsBack(double angle)
    {
        var store = new PlacementTextStore { Readback = item => { item.StartAngle = angle; return item; } };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(Request("arc"))).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Empty(store.Texts);
    }

    [Theory]
    [InlineData("polyline")]
    [InlineData("arc")]
    [InlineData("image")]
    public void NewGraphicsBounds_ParticipateInR2(string kind)
    {
        string nativeKind = kind == "polyline" ? "PolyLine" : kind == "arc" ? "Arc" : "Image";
        var model = new LayoutGuard.Model
        {
            Page = new() { GridSize = 1, PageSize = [100, 100], Placements = [LayoutGuardTests.Row("Function", 9, 20, 20)] }
        };
        DrawingPlacement graphic = LayoutGuard.Proposed(nativeKind, [20, 20], [18, 18, 24, 24]);
        Assert.Contains(LayoutGuard.Check(model, [graphic], suggest: false), v => v.Rule == "R2" && !v.Warning);
    }

    [Theory]
    [InlineData("PolyLine")]
    [InlineData("Arc")]
    [InlineData("Image")]
    public void NewGraphicsBounds_DoNotAcquireSymbolConnectionRules(string kind)
    {
        var model = new LayoutGuard.Model
        {
            Page = new() { GridSize = 1, PageSize = [100, 100] },
            Lines = [new() { Points = [[10, 20], [30, 20]], Changed = false }]
        };
        DrawingPlacement graphic = LayoutGuard.Proposed(kind, [20, 20], [18, 18, 24, 24]);
        Assert.DoesNotContain(LayoutGuard.Check(model, [graphic], suggest: false), v => v.Rule == "R4");
    }

    [Fact]
    public void Arc_UsesConservativeWholeCircleBoundsBeforeWrite()
    {
        var store = new PlacementTextStore();
        PlacementTextCreateArgs args = Request("arc");
        args.Items![0].Position = [2, 20];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error;
        Assert.Contains("R1", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Transactions.Calls);
    }

    internal static PlacementTextCreateArgs Request(string kind) => new()
    {
        Project = "Line1",
        Page = "P",
        Items = [kind switch
        {
            "polyline" => new() { Kind = kind, Position = [20, 20], Points = [[20, 20], [30, 30], [40, 20]] },
            "arc" => new() { Kind = kind, Position = [50, 50], RadiusMm = 5, StartAngle = 30, EndAngle = 150 },
            "path_text" => new() { Kind = kind, Position = [20, 20], HeightMm = 3.5, Contents = new() { ["??_??"] = "Motor circuit" } },
            _ => new() { Kind = kind, Position = [20, 20], EndPosition = [30, 30], File = "image.png", FilePath = Path.GetFullPath("image.png"), Sha256 = new('a', 64) }
        }]
    };
}
