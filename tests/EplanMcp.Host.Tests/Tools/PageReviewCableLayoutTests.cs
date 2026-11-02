using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageReviewCableLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStarRoutes_OffsetNodePin_SelectsSharedTrunkAndExclusiveABBranch(bool reverseSegments)
    {
        JsonObject observation = LiveObservation();
        if (reverseSegments)
        {
            foreach (JsonNode segment in observation["segments"]!.AsArray().Select(s => s!))
            {
                JsonNode startOid = segment["startOid"]!.DeepClone(), start = segment["start"]!.DeepClone();
                segment["startOid"] = segment["endOid"]!.DeepClone();
                segment["start"] = segment["end"]!.DeepClone();
                segment["endOid"] = startOid;
                segment["end"] = start;
            }
        }
        AssertLiveLayout(observation);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"unavailable\"")]
    [InlineData("0")]
    [InlineData("false")]
    public void NativeStarRoutes_UnrelatedConnectionWithoutChainIsSkipped(string path)
    {
        JsonObject observation = LiveObservation();
        observation["connections"]!.AsArray().Add(new JsonObject
        {
            ["oid"] = 72614,
            ["from"] = new JsonObject { ["oid"] = 69845, ["name"] = "=GAA+A1-R103D" },
            ["to"] = new JsonObject { ["oid"] = 69846, ["name"] = "=GAA+A1-R103E" },
            ["drawnPath"] = JsonNode.Parse(path),
        });
        observation["connections"]!.AsArray().Add(new JsonObject
        {
            ["oid"] = 72615,
            ["from"] = new JsonObject { ["oid"] = 69844, ["name"] = "=GAA+A1-R103C" },
            ["to"] = new JsonObject { ["oid"] = 69845, ["name"] = "=GAA+A1-R103D" },
        });
        AssertLiveLayout(observation);
    }

    [Fact]
    public void NativeStarRoutes_UniqueChainThroughMultipleNodes_SelectsSharedTrunk()
    {
        JsonObject observation = LiveObservation();
        observation["segments"]![2]!["endOid"] = 423459;
        observation["segments"]![2]!["end"] = new JsonArray(160, 160);
        observation["segments"]!.AsArray().Add(JsonNode.Parse("""
            { "startOid": 423459, "endOid": 69844, "start": [162,160], "end": [200,160] }
            """));
        AssertLiveLayout(observation);
    }

    [Fact]
    public void NativeStarRoutes_DirectSegment_SelectsSharedTrunk()
    {
        JsonObject observation = LiveObservation();
        observation["connections"]![1]!["to"] = new JsonObject { ["oid"] = 423458, ["name"] = "T" };
        AssertLiveLayout(observation);
    }

    [Fact]
    public void NativeStarRoutes_TargetWithoutChain_FailsWithConnectionAndSegments()
    {
        JsonObject observation = LiveObservation();
        observation["segments"]!.AsArray().RemoveAt(0);
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Read(observation));
        Assert.Contains("no segment chain", error.Message, StringComparison.Ordinal);
        AssertDiagnostic(error, observation);
    }

    [Fact]
    public void NativeStarRoutes_AmbiguousChain_FailsWithConnectionAndSegments()
    {
        JsonObject observation = LiveObservation();
        observation["segments"]!.AsArray().Add(JsonNode.Parse("""
            { "startOid": 69842, "endOid": 69843, "start": [120,220], "end": [120,100] }
            """));
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Read(observation));
        Assert.Contains("ambiguous segment chain", error.Message, StringComparison.Ordinal);
        AssertDiagnostic(error, observation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewEvidence_DrawnPath_SelectsSharedTrunkAndExclusiveABBranch(bool upperTrunk)
    {
        object End(string name) => new { name, pinName = "1", pinIndex = 0 };
        var ab = new { from = End("=GAA+A1-R103A"), to = End("=GAA+A1-R103B"), drawnPath = new double[][] { [120, 220], [120, 160], [120, 100] } };
        var other = new { from = End(upperTrunk ? "=GAA+A1-R103A" : "=GAA+A1-R103B"), to = End("=GAA+A1-R103C"), drawnPath = new double[][] { [120, upperTrunk ? 220 : 100], [120, 160], [200, 160] } };
        JsonElement observation = JsonSerializer.SerializeToElement(new { connections = new[] { other, ab } });
        var layout = PageReviewCableLayout.Read(observation, "=GAA+A1-R103A", "=GAA+A1-R103B", 4);
        Assert.Equal([80d, upperTrunk ? 192 : 128], layout.Trunk[0]);
        Assert.Equal([160d, upperTrunk ? 192 : 128], layout.Trunk[1]);
        Assert.Equal([80d, upperTrunk ? 128 : 192], layout.Branch[0]);
        Assert.Equal([160d, upperTrunk ? 128 : 192], layout.Branch[1]);
    }

    private static void AssertDiagnostic(Exception error, JsonObject observation)
    {
        Assert.Contains("72612", error.Message, StringComparison.Ordinal);
        Assert.Contains("=GAA+A1-R103A", error.Message, StringComparison.Ordinal);
        Assert.Contains("=GAA+A1-R103B", error.Message, StringComparison.Ordinal);
        Assert.Contains("Segments found:", error.Message, StringComparison.Ordinal);
        foreach (JsonNode segment in observation["segments"]!.AsArray().Select(s => s!))
        { Assert.Contains(segment.ToJsonString(), error.Message, StringComparison.Ordinal); }
    }

    private static (double[][] Trunk, double[][] Branch) Read(JsonObject observation) =>
        PageReviewCableLayout.Read(JsonSerializer.SerializeToElement(observation), "=GAA+A1-R103A", "=GAA+A1-R103B", 4);

    private static void AssertLiveLayout(JsonObject observation)
    {
        var layout = Read(observation);
        Assert.Equal([80d, 128], layout.Trunk[0]);
        Assert.Equal([160d, 128], layout.Trunk[1]);
        Assert.Equal([80d, 192], layout.Branch[0]);
        Assert.Equal([160d, 192], layout.Branch[1]);
    }

    private static JsonObject LiveObservation() => JsonNode.Parse("""
        {
          "devices": [
            { "page": "=GAA+A1&EFS1/103", "oid": 69842, "name": "=GAA+A1-R103A", "position": [120,224] },
            { "page": "=GAA+A1&EFS1/103", "oid": 69843, "name": "=GAA+A1-R103B", "position": [120,96] },
            { "page": "=GAA+A1&EFS1/103", "oid": 69844, "name": "=GAA+A1-R103C", "position": [204,160] }
          ],
          "segments": [
            { "startOid": 423458, "endOid": 69843, "start": [120,160], "end": [120,100] },
            { "startOid": 423458, "endOid": 69842, "start": [120,160], "end": [120,220] },
            { "startOid": 423458, "endOid": 69844, "start": [122,160], "end": [200,160] }
          ],
          "connections": [
            { "oid": 72612, "from": { "oid": 69842, "name": "=GAA+A1-R103A", "pinIndex": 1 }, "to": { "oid": 69843, "name": "=GAA+A1-R103B" } },
            { "oid": 72613, "from": { "oid": 69843, "name": "=GAA+A1-R103B", "pinIndex": 0 }, "to": { "oid": 69844, "name": "=GAA+A1-R103C" } }
          ]
        }
        """)!.AsObject();
}
