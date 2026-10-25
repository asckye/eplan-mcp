using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_plan_get paging, strict cursors, full cleaned fragments and complete-result budgets.</summary>
[Trait("Size", "Small")]
[Trait("Tool", PlanTools.GetTool)]
public sealed class PlanReadModelTests
{
    private static PlanRecord Record() => new(1, PlanCanonical.Hash(PlanTestData.Definition()), PlanTestData.Definition(), new());

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(200)]
    public void Detailed_PagesWholeEntriesWithExactTotal_AndBothJsonCopiesFit(int limit)
    {
        PlanRecord record = Record();
        var actual = new List<string>();
        string? cursor = null;
        do
        {
            CallToolResult call = PlanReadModel.Page(record, "detailed", limit, cursor);
            JsonElement data = ContextToolTests.Data(call);
            Assert.Equal(PlanEntries.Build(record, true).Count, data.GetProperty("total").GetInt32());
            JsonElement[] entries = [.. data.GetProperty("entries").EnumerateArray()];
            Assert.InRange(entries.Length, 1, limit);
            actual.AddRange(entries.Select(e => e.GetRawText()));
            Assert.True(JsonElement.DeepEquals(data, ContextToolTests.Parse(((TextContentBlock)call.Content[1]).Text)));
            Assert.InRange(Tokens(call), 1, 20000);
            cursor = data.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);
        Assert.Equal(PlanEntries.Build(record, true).Select(e => e.ToJsonString(HostJson.Options)), actual);
    }

    [Fact]
    public void LargeValue_FragmentsReassembleFullCleanedJson_WithoutChangingHashOrSplittingScalars()
    {
        PlanRecord original = Record();
        string text = string.Concat(Enumerable.Repeat("a\u0001\u202e\U0001F600\t\n", 1800));
        JsonElement args = JsonSerializer.SerializeToElement(new { description = text });
        PlanDefinition definition = original.Definition with { Steps = [original.Definition.Steps[0] with { Args = args }] };
        PlanRecord record = original with { Definition = definition, PlanHash = PlanCanonical.Hash(definition) };
        string before = PlanCanonical.Serialize(definition);
        var fragments = new List<JsonElement>();
        string? cursor = null;
        int calls = 0;
        do
        {
            CallToolResult call = PlanReadModel.Page(record, "detailed", 200, cursor, 5000);
            JsonElement data = ContextToolTests.Data(call);
            Assert.InRange(Tokens(call), 1, 5000);
            Assert.NotEmpty(data.GetProperty("entries").EnumerateArray());
            fragments.AddRange(data.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("key").GetString() == "args"));
            cursor = data.GetProperty("nextCursor").GetString();
            Assert.True(++calls < 200);
        }
        while (cursor is not null);
        Assert.True(calls > 1);
        Assert.Equal(fragments.Count, fragments[0].GetProperty("fragmentCount").GetInt32());
        Assert.Equal(Enumerable.Range(0, fragments.Count), fragments.Select(e => e.GetProperty("fragmentIndex").GetInt32()));
        var strict = new UTF8Encoding(false, true);
        Assert.All(fragments, e => Assert.NotEmpty(strict.GetBytes(e.GetProperty("value").GetString()!)));
        string reconstructed = string.Concat(fragments.Select(e => e.GetProperty("value").GetString()));
        Assert.Equal(ResultShaper.CleanFullText(text), ContextToolTests.Parse(reconstructed).GetProperty("description").GetString());
        Assert.Equal(before, PlanCanonical.Serialize(definition));
        Assert.Equal(PlanCanonical.Hash(definition), record.PlanHash);
    }

    [Theory]
    [InlineData("planId", "another")]
    [InlineData("planHash", "another")]
    [InlineData("revision", "number")]
    [InlineData("responseFormat", "concise")]
    [InlineData("offset", "number")]
    [InlineData("extra", "unknown")]
    public void Cursor_BoundFieldsAndUnknownMembers_AreStrictlyRejected(string field, string value)
    {
        PlanRecord record = Record();
        string cursor = ContextToolTests.Data(PlanReadModel.Page(record, "detailed", 1, null)).GetProperty("nextCursor").GetString()!;
        JsonNode node = JsonNode.Parse(Base64Url.DecodeFromChars(cursor))!;
        node[field] = value == "number" ? JsonValue.Create(999999) : JsonValue.Create(value);
        AssertCursorError(record, Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(node)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("bnVsbA")]
    [InlineData("e30")]
    public void Cursor_Malformed_IsAnActionableReadError(string cursor) => AssertCursorError(Record(), cursor);

    [Fact]
    public void Cursor_DuplicateMembersPaddingAndRevisionChange_AreRejected()
    {
        PlanRecord record = Record();
        string cursor = ContextToolTests.Data(PlanReadModel.Page(record, "detailed", 1, null)).GetProperty("nextCursor").GetString()!;
        string json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        AssertCursorError(record, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json.Insert(1, "\"offset\":1,"))));
        AssertCursorError(record, cursor + "=");
        AssertCursorError(record with { State = record.State with { Revision = 1 } }, cursor);
    }

    [Fact]
    public void Budget_NecessaryEnvelopeTooLarge_ReturnsMinimumWithoutARepeatingCursor()
    {
        CallToolResult call = PlanReadModel.Page(Record(), "detailed", 50, null, 1);
        JsonElement error = ContextToolTests.Error(call);
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.True(error.GetProperty("metadata").GetProperty("minimumEstimatedTokens").GetInt32() > 1);
        Assert.False(call.StructuredContent!.Value.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public void Result_ConciseRetainsFailuresOutsideAlertsAndReference_DetailedSortsFixedSections()
    {
        PlanResult result = TaskTestData.Result(ExecutionState.Unknown, "skipped", ErrorReason.OutcomeUnknown) with
        {
            Validation = PlanTestData.Json("""[{"step":2,"locator":"z","check":"b","status":"failed"},{"step":1,"locator":"a","check":"a","status":"passed"}]"""),
            Diff = PlanTestData.Json("""[{"step":2,"class":"outside_scope","locator":"z","property":"a"},{"step":1,"class":"planned_applied","locator":"a","property":"b"}]"""),
            Backups = [new("backup", 1, 2, true)],
        };
        PlanRecord record = Record() with { State = new(Revision: 4, Status: PlanStatus.Unknown, Result: result) };
        JsonElement data = ContextToolTests.Data(PlanReadModel.Page(record, "concise", 50, null));
        Assert.False(data.TryGetProperty("execution", out _));
        Assert.Equal("unknown", data.GetProperty("result").GetProperty("execution").GetString());
        Assert.Equal(ErrorReason.OutcomeUnknown, data.GetProperty("result").GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(1, data.GetProperty("summary").GetProperty("outsideScope").GetInt32());
        Assert.Equal(2, data.GetProperty("result").GetProperty("validation").GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("result").GetProperty("validation").GetProperty("failed").GetInt32());
        Assert.Contains(data.GetProperty("entries").EnumerateArray(), e => e.GetProperty("section").GetString() == "diff");
        List<JsonObject> all = PlanEntries.Build(record, true);
        Assert.Equal([1, 2], all.Where(e => e["section"]!.GetValue<string>() == "validation").Select(e => e["index"]!.GetValue<int>()));
        Assert.Equal([1, 2], all.Where(e => e["section"]!.GetValue<string>() == "diff").Select(e => e["index"]!.GetValue<int>()));
        Assert.Equal("eplanmcp://plans/" + record.Definition.PlanId, data.GetProperty("resource").GetString());
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("diff")]
    public void SectionSummary_PreservesMetadataAndCoverage_WithoutRepeatingEntries(string section)
    {
        JsonNode node = JsonNode.Parse("""{"entries":[{"status":"failed"}],"coverage":{"electrical":"skipped","drawing":"skipped"}}""")!;
        JsonObject summary = PlanEntries.Summary(node)!;
        Assert.Equal(1, summary["total"]!.GetValue<int>());
        Assert.Equal("skipped", summary["coverage"]!["electrical"]!.GetValue<string>());
        Assert.Null(summary["entries"]);
        Assert.Single(node["entries"]!.AsArray());
        PlanResult result = TaskTestData.Result() with
        {
            Validation = section == "validation" ? JsonSerializer.SerializeToElement(node) : null,
            Diff = section == "diff" ? JsonSerializer.SerializeToElement(node) : null,
        };
        JsonElement data = ContextToolTests.Data(PlanReadModel.Page(Record() with { State = new(Result: result) }, "detailed", 50, null));
        Assert.Equal("skipped", data.GetProperty("result").GetProperty(section).GetProperty("coverage").GetProperty("electrical").GetString());
    }

    [Theory]
    [InlineData("concise", 0, null)]
    [InlineData("detailed", 201, null)]
    [InlineData("other", 50, null)]
    [InlineData("concise", 50, "cursor")]
    public void InvalidArguments_ReturnReadError(string format, int limit, string? cursor)
    {
        Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(PlanReadModel.Page(Record(), format, limit, cursor)).GetProperty("reason").GetString());
    }

    private static void AssertCursorError(PlanRecord record, string cursor)
    {
        JsonElement error = ContextToolTests.Error(PlanReadModel.Page(record, "detailed", 50, cursor));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("first page", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(error.TryGetProperty("execution", out _));
    }

    private static int Tokens(CallToolResult call) => ResultShaper.ResultTokens(call.StructuredContent!.Value.GetRawText())
        + ResultShaper.EstimateTokens(((TextContentBlock)call.Content[0]).Text)
        + ResultShaper.EstimateTokens(JsonSerializer.Serialize(call.Content[2], HostJson.Options));
}
