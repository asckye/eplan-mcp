using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementRotationRegressionTests
{
    [Theory]
    [InlineData("arc")]
    [InlineData("ellipse")]
    public void ArcAndEllipse_NegativeAnglesAreOrdinaryDegrees(string kind)
    {
        var store = new PlacementTextStore
        {
            Readback = item =>
            {
                if (kind == "arc")
                { item.StartAngle = 198; }
                else
                { item.Angle = 198; }
                return item;
            }
        };
        PlacementTextCreateArgs args = PlacementGraphicsCreateTests.Request(kind);
        if (kind == "arc")
        { args.Items![0].StartAngle = -16002; }
        else
        { args.Items![0].Angle = -16002; }
        Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args));
        Assert.Single(store.Texts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    public void EqualCircleAngles_MustReadBackBothAngles(double angle)
    {
        var store = new PlacementTextStore { Readback = item => { item.StartAngle = 0; item.EndAngle = 0; return item; } };
        PlacementTextCreateArgs args = PlacementGraphicsCreateTests.Request("arc");
        args.Items![0].StartAngle = args.Items[0].EndAngle = angle;
        if (angle == 0)
        { Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args)); }
        else
        {
            Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error.Reason);
            Assert.Empty(store.Texts);
        }
    }

    [Theory]
    [InlineData(450, 90, true)]
    [InlineData(-90, 270, true)]
    [InlineData(-16002, -16002, true)]
    [InlineData(-16002, 197.99, false)]
    [InlineData(90, 91, false)]
    public void Rotation_UsesModulo360ExceptFromLayer(double wanted, double read, bool accepted)
    {
        var store = new PlacementTextStore { Readback = item => { item.Angle = read; return item; } };
        PlacementTextCreateArgs args = PlacementTextStore.Args();
        args.Text.Angle = wanted;
        if (accepted)
        { Assert.Equal(read, Assert.IsType<PlacementTextCreateData>(store.Operation.Execute(args)).Text.Angle); }
        else
        { Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => store.Operation.Execute(args)).Error.Reason); }
        Assert.Equal(accepted ? 1 : 0, store.Texts.Count);
    }
}
