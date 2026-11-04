using EplanMcp.Large.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SmallProjectRedrawCallTests
{
    [Fact]
    public async Task ConnectionGeneration_EmptyLiveResultIsRecordedWithoutFailure()
    {
        JObject source = Page();
        JObject emptyGeneration = JObject.Parse("""
            {
              "project": "ScratchTest", "execution": "not_started", "dryRun": false,
              "fingerprint": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "changes": [{ "action": "update", "kind": "page", "name": "/1",
                "property": "connections", "oldValue": "0", "newValue": null }],
              "pages": [{ "page": "/1", "before": 0, "after": 0, "added": [], "removed": [] }],
              "undo": null, "recovered": false, "truncated": false,
              "hint": "EPLAN added and removed no connections on these pages. If lines drawn there still do not show as connections, ask the engineer to check EPLAN's messages for these pages."
            }
            """);
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = new SmallProjectRedrawDriver((tool, _) => Task.FromResult(tool == "eplan_connection_generate"
            ? emptyGeneration : Success()), "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        JObject result = report.ToJson();
        Assert.Equal(0, (int)result["failedToolCallCount"]!);
        JToken generation = Assert.Single((JArray)result["calls"]!, item => (string?)item["tool"] == "eplan_connection_generate");
        Assert.True((bool)generation["succeeded"]!);
        Assert.True(JToken.DeepEquals(emptyGeneration, generation["result"]));
        Assert.Contains(((JArray)result["notes"]!).OfType<JObject>(), item => (string?)item["objectType"] == "Connection");
    }

    [Theory]
    [InlineData("TIMEOUT", "not_started")]
    [InlineData("EPLAN_OPERATION_FAILED", "rolled_back")]
    public async Task ConnectionGeneration_ErrorStillCountsAsFailure(string reason, string execution)
    {
        JObject source = Page();
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var driver = new SmallProjectRedrawDriver((tool, _) => Task.FromResult(tool == "eplan_connection_generate"
            ? Failure(reason, execution) : Success()), "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal(1, (int)report.ToJson()["failedToolCallCount"]!);
    }

    [Fact]
    public async Task Graphics_InvalidBatchRetriesEachItemOnceAndIdentifiesTheRejectedObject()
    {
        JObject source = Page(Line(1), InvalidText(2));
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        var batches = new List<JObject>();
        Task<JObject> CallAsync(string tool, JObject args)
        {
            if (tool != "eplan_placement_create")
            { return Task.FromResult(Success()); }
            batches.Add((JObject)args.DeepClone());
            var items = (JArray)args["items"]!;
            return Task.FromResult(items.Count > 1 || (string?)items[0]?["kind"] == "text"
                ? Failure("INVALID_ARGUMENT", "not_started") : Success());
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal([2, 1, 1], batches.Select(args => ((JArray)args["items"]!).Count));
        Assert.True(JToken.DeepEquals(batches[0]["items"]![0], batches[1]["items"]![0]));
        Assert.True(JToken.DeepEquals(batches[0]["items"]![1], batches[2]["items"]![0]));
        Assert.All(batches, args =>
        {
            Assert.False((bool)args["dry_run"]!);
            Assert.False((bool)args["snap_to_grid"]!);
            Assert.Equal("ScratchTest", (string?)args["project"]);
        });
        JObject result = report.ToJson();
        Assert.Equal(2, (int)result["failedToolCallCount"]!);
        JToken rejected = Assert.Single((JArray)result["calls"]!, item => !(bool)item["succeeded"]!
            && ((JArray)item["objectOids"]!).Count == 1);
        Assert.Equal("2", (string?)rejected["objectOids"]![0]);
        Assert.Contains(((JArray)result["calls"]!).OfType<JObject>(), item => (string?)item["tool"] == "eplan_page_review");
    }

    [Theory]
    [InlineData("TIMEOUT", "not_started")]
    [InlineData("INVALID_ARGUMENT", "unknown")]
    [InlineData("INVALID_ARGUMENT", "rolled_back")]
    [InlineData("EPLAN_OPERATION_FAILED", "not_started")]
    public async Task Graphics_TimeoutOrUnconfirmedValidationFailureIsNotReplayed(string reason, string execution)
    {
        JObject source = Page(Line(1), InvalidText(2));
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        int placements = 0;
        Task<JObject> CallAsync(string tool, JObject _)
        {
            if (tool != "eplan_placement_create")
            { return Task.FromResult(Success()); }
            placements++;
            return Task.FromResult(Failure(reason, execution));
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal(1, placements);
        Assert.Equal(1, (int)report.ToJson()["failedToolCallCount"]!);
    }

    [Fact]
    public async Task Graphics_ThrownTransportFailureIsNotReplayed()
    {
        JObject source = Page(Line(1), Line(2));
        var report = new SmallProjectRedrawReport("test", "ScratchTest");
        int placements = 0;
        Task<JObject> CallAsync(string tool, JObject _)
        {
            if (tool != "eplan_placement_create")
            { return Task.FromResult(Success()); }
            placements++;
            throw new IOException("Transport result is unknown.");
        }
        var driver = new SmallProjectRedrawDriver(CallAsync, "ScratchTest", Path.GetTempPath(), report);

        await driver.DrawPageAsync(source, [source]);

        Assert.Equal(1, placements);
        Assert.Equal(1, (int)report.ToJson()["failedToolCallCount"]!);
    }

    private static JObject Page(params JObject[] objects) => new()
    {
        ["name"] = "/1",
        ["pageType"] = "Circuit",
        ["placements"] = new JArray(objects),
        ["connections"] = new JArray(),
    };

    private static JObject Line(int oid) => new()
    {
        ["type"] = "Line",
        ["oid"] = oid,
        ["location"] = Point(284, 188),
        ["start"] = Point(284, 188),
        ["end"] = Point(287, 185.75),
    };

    private static JObject InvalidText(int oid) => new()
    {
        ["type"] = "Text",
        ["oid"] = oid,
        ["location"] = Point(42, 168),
        ["height"] = -16002,
        ["content"] = new JObject { ["en_US"] = "Fixture" },
    };

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };

    private static JObject Success() => new() { ["execution"] = "committed" };

    private static JObject Failure(string reason, string execution) => new()
    {
        ["error"] = new JObject
        {
            ["reason"] = reason,
            ["execution"] = execution,
            ["retryable"] = false,
            ["message"] = "Invalid arguments: /items/1/height_mm: must be a finite number in range.",
            ["metadata"] = new JObject { ["violations"] = "/items/1/height_mm: must be a finite number in range." },
        },
    };
}
