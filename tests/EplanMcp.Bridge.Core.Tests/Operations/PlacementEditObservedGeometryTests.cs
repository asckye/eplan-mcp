using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlacementEditObservedGeometryTests
{
    // q-edit-PlacementEditSpikeTests.log: the product request selects the first of these three rectangles.
    [Theory]
    [InlineData("rotate", 30, 200, 40, 210)]
    [InlineData("mirror", 40, 90, 50, 100)]
    public void ProductRectangles_NativeCornerNormalization_CommitsWithExactReadback(string operation, double left, double bottom, double right, double top)
    {
        PlacementEditFixture fake = Rectangles();
        JObject input = fake.Input(operation);
        input["response_format"] = "detailed";
        if (operation == "rotate")
        { input["quarter_turns"] = 1; }
        else
        { input["axis"] = "horizontal"; input["coordinate_mm"] = 150; }
        JObject plan = PlacementEditPlanner.Plan(input, fake.Data);
        JToken predicted = Assert.Single(plan["predicted"]!);
        Assert.Equal([left, bottom], predicted["location"]!.Values<double>());
        Assert.Equal([left, bottom, right, top], predicted["bounds"]!.Values<double>());
        Assert.Equal(0, (double)predicted["angle"]!);
        fake.Mutation = () => SetGeometry(fake, 0, [left, bottom], [left, bottom, right, top], 0);

        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = input, ["fingerprint"] = plan["fingerprint"] })!;

        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal(1, fake.Transactions);
        Assert.Equal(1, fake.Closed);
        Assert.True(JToken.DeepEquals(result["before"]!["objects"]![1], result["after"]!["objects"]![1]));
        Assert.True(JToken.DeepEquals(result["before"]!["objects"]![2], result["after"]!["objects"]![2]));
    }

    [Theory]
    [InlineData("location")]
    [InlineData("bounds")]
    [InlineData("angle")]
    public void ProductRectangle_NormalizedShapeStillRejectsAnyReadbackDifference(string field)
    {
        PlacementEditFixture fake = Rectangles();
        JObject input = fake.Input("rotate");
        input["quarter_turns"] = 1;
        string fingerprint = (string)PlacementEditPlanner.Plan(input, fake.Data)["fingerprint"]!;
        JObject baseline = (JObject)fake.Data.DeepClone();
        fake.Mutation = () =>
        {
            SetGeometry(fake, 0, [30, 200], [30, 200, 40, 210], 0);
            JToken row = fake.Data["objects"]![0]!;
            if (field == "angle")
            { row[field] = 1d; }
            else
            { row[field]![0] = (double)row[field]![0]! + 1; }
        };

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = input, ["fingerprint"] = fingerprint }));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.True(JToken.DeepEquals(baseline, fake.Data));
        Assert.Equal(0, fake.Closed);
    }

    [Theory]
    [InlineData("vertical", 210, 320, 200, 291.07, 200, 320, 205.99025)]
    [InlineData("horizontal", 150, 100, 100, 100, 94.00975, 128.93, 100)]
    public void NativeMirrorNote_PreservesZeroAngleAndReflectsTheFullOutline(string axis, double coordinate, double x, double y, double left, double bottom, double right, double top)
    {
        var fake = new PlacementEditFixture();
        fake.Data["objects"] = new JArray(fake.Data["objects"]![0]!.DeepClone());
        fake.Data["scene"]!["page"]!["placements"] = new JArray(fake.Data["scene"]!["page"]!["placements"]![0]!.DeepClone());
        SetGeometry(fake, 0, [100, 200], [100, 200, 128.93, 205.99025], 0);
        fake.Data["objects"]![0]!["properties"]!["text"] = "??_??@Mirror note;";
        JObject input = fake.Input("mirror");
        input["axis"] = axis;
        input["coordinate_mm"] = coordinate;
        JObject plan = PlacementEditPlanner.Plan(input, fake.Data);
        Assert.Equal([x, y], plan["predicted"]![0]!["location"]!.Values<double>());
        Assert.Equal([left, bottom, right, top], plan["predicted"]![0]!["bounds"]!.Values<double>());
        Assert.Equal(0, (double)plan["predicted"]![0]!["angle"]!);
        fake.Mutation = () => SetGeometry(fake, 0, [x, y], [left, bottom, right, top], 0);
        JObject result = (JObject)new PlacementEditExecuteOperation(fake, fake, fake.Backup).Execute(new JObject { ["request"] = input, ["fingerprint"] = plan["fingerprint"] })!;
        Assert.Equal(ExecutionState.Committed, (string?)result["execution"]);
        Assert.Equal("??_??@Mirror note;", (string?)fake.Data["objects"]![0]!["properties"]!["text"]);
        Assert.Equal(1, fake.Closed);
    }

    private static PlacementEditFixture Rectangles()
    {
        var fake = new PlacementEditFixture();
        fake.Data["scene"]!["page"]!["pageSize"] = new JArray(420, 297.02);
        fake.Data["scene"]!["page"]!["gridSize"] = 4;
        int[] xs = [40, 100, 180];
        for (int i = 0; i < xs.Length; i++)
        {
            fake.Data["objects"]![i]!["kind"] = "Rectangle";
            fake.Data["objects"]![i]!["properties"] = new JObject { ["grouped"] = "false" };
            fake.Data["scene"]!["page"]!["placements"]![i]!["kind"] = "Rectangle";
            SetGeometry(fake, i, [xs[i], 200 - i * 20], [xs[i], 200 - i * 20, xs[i] + 10, 210 - i * 20], 0);
        }
        return fake;
    }

    private static void SetGeometry(PlacementEditFixture fake, int index, double[] location, double[] bounds, double angle)
    {
        JToken row = fake.Data["objects"]![index]!;
        row["location"] = new JArray(location);
        row["bounds"] = new JArray(bounds);
        row["angle"] = angle;
        fake.Data["scene"]!["page"]!["placements"]![index]!["location"] = new JArray(location);
        fake.Data["scene"]!["page"]!["placements"]![index]!["bounds"] = new JArray(bounds);
    }
}
