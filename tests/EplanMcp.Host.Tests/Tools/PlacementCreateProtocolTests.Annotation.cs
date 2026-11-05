using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData("linear", null)]
    [InlineData("angular", true)]
    [InlineData("baseline", false)]
    [InlineData("incremental", false)]
    [InlineData("continued", false)]
    [InlineData("circle", false)]
    public async Task AnnotationDimensions_MapOfficialArgumentsAndReadCalculatedText(string type, bool? dryRun)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            if (dryRun.HasValue)
            { args["dry_run"] = dryRun.Value; }
            else
            { args.Remove("dry_run"); }
            string fields = type == "circle" ? "\"arc\":{\"project\":\"Line1\",\"page\":\"P\",\"type\":1,\"oid\":2,\"digest\":\"" + new string('a', 64) + "\"}"
                : type is "linear" or "angular" ? "\"first\":[10,20],\"second\":[30,20]" : "\"points\":[[10,20],[30,20],[50,20]]";
            args["items"] = JsonSerializer.Deserialize<JsonElement>("[{\"kind\":\"dimension\",\"dimension_type\":\"" + type + "\",\"end\":[30,40]," + fields + "}]");
            JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(AnnotationCreateTools.Tool, args, cancellationToken: Token));
            PlacementTextCreateArgs request = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            PlacementText dimension = Assert.Single(request.Items!);
            Assert.Equal(type, dimension.DimensionType);
            Assert.Equal([30d, 40d], dimension.EndPosition);
            Assert.Equal(type == "circle" ? 0 : type is "linear" or "angular" ? 2 : 3, dimension.Points.Length);
            Assert.Null(dimension.DimensionText);
            if (dryRun == false)
            { Assert.Equal(type == "circle" ? 1 : 2, data.GetProperty("items")[0].GetProperty("actual").GetProperty("dimension_text").GetArrayLength()); }
            else
            { Assert.Empty(store.Items); Assert.Empty(harness.Bridge.Writes); }
        });
    }

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
                [{"kind":"hyperlink","position":[10,10],"link":"https://example.test","contents":{"??_??":"Reference"}},
                 {"kind":"qr_code","position":[25,10],"size_mm":5,"text":"Panel"},
                 {"kind":"spline","points":[[40,10],[45,15],[50,10]]},
                 {"kind":"sector","center":[60,15],"radius_mm":5,"start_angle":0,"end_angle":90},
                 {"kind":"dimension","dimension_type":"linear","first":[10,30],"second":[20,30],"end":[20,40]},
                 {"kind":"placeholder","position":[40,40],"name":"Variant","records":["A"],"variables":["Part"]},
                 {"kind":"macro_box","position":[65,40],"macro_name":"Panel.ema"}]
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
            Assert.Equal("A", Assert.Single(request.Items[5].Records!));
            Assert.Equal("Panel.ema", request.Items[6].Name);
            if (preview)
            { Assert.Empty(store.Calls); Assert.Empty(store.Items); }
            else
            { Assert.Equal(["Begin AI: eplan_annotation_create", "Commit", "CloseUndo", "Dispose"], store.Calls); }
            Assert.True(PropertyGetContractTests.Accepts(AnnotationCreateContract.Definition.OutputSchema!.Value, data, AnnotationCreateContract.Definition.OutputSchema.Value));
        });
    }

    [Fact]
    public async Task AnnotationFailure_RollsBackWholeBatchWithSharedProtocol()
    {
        await WithClient(async (client, harness, store) =>
        {
            store.Fail = true;
            Dictionary<string, object?> args = Args();
            args["items"] = JsonSerializer.Deserialize<JsonElement>("""[{"kind":"placeholder","position":[20,20],"name":"A"},{"kind":"macro_box","position":[50,50],"macro_name":"B"}]""");
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(AnnotationCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
            Assert.Empty(store.Items);
            Assert.Single(harness.Bridge.Writes);
            Assert.Equal(["Begin AI: eplan_annotation_create", "Rollback", "Dispose"], store.Calls);
        });
    }
}
