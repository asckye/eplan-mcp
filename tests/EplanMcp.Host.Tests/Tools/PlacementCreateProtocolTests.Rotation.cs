using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CircleAndFromLayerRotation_PreserveProtocolValues(bool preview)
    {
        await WithClient(async (client, harness, store) =>
        {
            var args = Args();
            args["dry_run"] = preview;
            args["items"] = new object[]
            {
                new { kind = "arc", center = (double[])[50, 50], radius_mm = 3, start_angle = 180, end_angle = 180 },
                new { kind = "text", position = (double[])[20, 20], height_mm = 3.5, angle = -16002, contents = new Dictionary<string, string> { ["??_??"] = "Rotation" } }
            };
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            var planned = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            Assert.Equal((180d, 180d, -16002d), (planned.Items![0].StartAngle, planned.Items[0].EndAngle, planned.Items[1].Angle));
            Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
        });
    }
}
