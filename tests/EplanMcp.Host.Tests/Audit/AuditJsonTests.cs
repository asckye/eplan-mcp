using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;

namespace EplanMcp.Host.Tests.Audit;

/// <summary>The JSON of <c>audit verify --format json</c> (architecture 5.2) for every result of the check.</summary>
[Trait("Size", "Small")]
public sealed class AuditJsonTests
{
    private const string AuditDirectory = @"D:\EplanMcp\audit";

    [Fact]
    public void FormatJson_AnIntactChain_HasNoBreak()
    {
        JsonObject json = Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, intactRecords: 3, broken: null));

        Assert.Equal(["schemaVersion", "directory", "exists", "intactRecords", "intact", "break"], json.Select(p => p.Key));
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal(AuditDirectory, json["directory"]!.GetValue<string>());
        Assert.True(json["exists"]!.GetValue<bool>());
        Assert.Equal(3, json["intactRecords"]!.GetValue<int>());
        Assert.True(json["intact"]!.GetValue<bool>());
        Assert.Null(json["break"]);
    }

    [Theory]
    [InlineData("Changed", "changed")]
    [InlineData("Missing", "missing")]
    [InlineData("NotARecord", "not_a_record")]
    public void FormatJson_ABrokenChain_SaysWhereAndWhy(string problem, string name)
    {
        var broken = new AuditBreak("2026-10-20.jsonl", 2, Enum.Parse<AuditProblem>(problem));

        JsonObject json = Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, intactRecords: 1, broken));

        Assert.False(json["intact"]!.GetValue<bool>());
        Assert.Equal(1, json["intactRecords"]!.GetValue<int>());
        JsonObject at = json["break"]!.AsObject();
        Assert.Equal(["file", "line", "problem"], at.Select(p => p.Key));
        Assert.Equal(("2026-10-20.jsonl", 2, name), (at["file"]!.GetValue<string>(), at["line"]!.GetValue<int>(), at["problem"]!.GetValue<string>()));
    }

    [Fact]
    public void FormatJson_NamesEveryProblemThereIs()
    {
        // A new problem needs a name of its own, which changes the shape (a new schemaVersion).
        Assert.Equal(["not_a_record", "missing", "changed"], Enum.GetValues<AuditProblem>().Select(p =>
            Parse(AuditCommand.FormatJson(AuditDirectory, exists: true, 0, new AuditBreak("f", 1, p)))["break"]!["problem"]!.GetValue<string>()));
    }

    [Fact]
    public void FormatJson_NoAuditDirectory_IsIntactWithoutRecords()
    {
        JsonObject json = Parse(AuditCommand.FormatJson(AuditDirectory, exists: false, intactRecords: 0, broken: null));

        Assert.False(json["exists"]!.GetValue<bool>());
        Assert.Equal(0, json["intactRecords"]!.GetValue<int>());
        Assert.True(json["intact"]!.GetValue<bool>());
        Assert.Null(json["break"]);
    }

    private static JsonObject Parse(string json)
    {
        Assert.DoesNotContain('\n', json);
        return JsonNode.Parse(json)!.AsObject();
    }
}
