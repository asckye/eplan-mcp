using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnnotationBatch_UsesSharedPreviewApprovalAndSingleUndo(bool? dryRun)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            if (dryRun.HasValue)
            { args["dry_run"] = dryRun.Value; }
            else
            { args.Remove("dry_run"); }
            args["response_format"] = "detailed";
            args["items"] = JsonSerializer.Deserialize<JsonElement>("""
                [{"kind":"hyperlink","position":[10,10],"link":"https://example.test","contents":{"??_??":"Reference"}},{"kind":"qr_code","position":[25,10],"size_mm":5,"text":"Panel"},{"kind":"spline","points":[[40,10],[45,15],[50,10]]},{"kind":"sector","center":[60,15],"radius_mm":5,"start_angle":0,"end_angle":90}]
                """);
            JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(AnnotationCreateTools.Tool, args, cancellationToken: Token));
            bool preview = dryRun != false;
            Assert.Equal(preview, data.GetProperty("dryRun").GetBoolean());
            Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
            Assert.Equal(preview ? 0 : 1, harness.Bridge.Writes.Count);
            PlacementTextCreateArgs request = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            Assert.True(request.Annotations);
            Assert.Equal(AnnotationCreatePlanning.Kinds, request.Items!.Select(i => i.Kind));
            Assert.Equal("https://example.test", request.Items![0].Link);
            Assert.Equal((5d, "Panel"), (request.Items[1].SizeMm, request.Items[1].Text));
            if (preview)
            { Assert.Empty(store.Calls); Assert.Empty(store.Items); }
            else
            { Assert.Equal(["Begin AI: eplan_annotation_create", "Commit", "CloseUndo", "Dispose"], store.Calls); }
            Assert.True(PropertyGetContractTests.Accepts(AnnotationCreateContract.Definition.OutputSchema!.Value, data, AnnotationCreateContract.Definition.OutputSchema.Value));
        });
    }

}
