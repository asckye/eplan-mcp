using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertySchemaToolTests
{
    private readonly ManualTime _time = new(DateTimeOffset.UnixEpoch);
    private PropertySchemaPaging Paging => new(_time);

    [Fact]
    public void Paging_StableOrderingAndTotals_ContinueWithoutSkipping()
    {
        PropertySchemaPaging paging = Paging;
        PropertyDictionaryEntry[] expected = [.. PropertyCatalog.Entries.Where(e => e.ObjectType == "Function" && e.ReadRefusal is null)
            .OrderBy(e => e.PropertyId).ThenBy(e => e.PropertyName, StringComparer.Ordinal)];
        JsonElement first = Data(Query(paging, "Function", limit: 3));
        string cursor = first.GetProperty("cursor").GetString()!;
        JsonElement second = Data(Query(paging, "Function", limit: 3, cursor: cursor));
        Assert.Equal(expected.Length, first.GetProperty("total").GetInt32());
        Assert.Equal(expected.Take(3).Select(e => e.PropertyId), Items(first).Select(e => e.GetProperty("property_id").GetInt32()).Cast<int?>());
        Assert.Equal(expected.Skip(3).Take(3).Select(e => e.PropertyId), Items(second).Select(e => e.GetProperty("property_id").GetInt32()).Cast<int?>());
        Assert.True(first.GetProperty("truncated").GetBoolean());
        Assert.Equal(first.GetRawText(), Data(Query(paging, "Function", limit: 3)).GetRawText());
    }

    [Fact]
    public void Cursor_BindsFiltersTypeSelectorsFormatLimitAndHost_AndRejectsTamperingAndExpiry()
    {
        PropertySchemaPaging paging = Paging;
        string cursor = Data(Query(paging, "Function", limit: 1)).GetProperty("cursor").GetString()!;
        Invalid(Query(paging, "Page", limit: 1, cursor: cursor));
        Invalid(Query(paging, "Function", name_contains: "TEXT", limit: 1, cursor: cursor));
        Invalid(Query(paging, "Function", Json("[{\"property_id\":20011}]"), limit: 1, cursor: cursor));
        Invalid(Query(paging, "Function", response_format: "detailed", limit: 1, cursor: cursor));
        Invalid(Query(paging, "Function", limit: 2, cursor: cursor));
        Invalid(Query(Paging, "Function", limit: 1, cursor: cursor));
        Invalid(Query(paging, "Function", limit: 1, cursor: "!"));
        Invalid(Query(paging, "Function", limit: 1, cursor: new string('x', 1025)));
        Invalid(Query(paging, "Function", limit: 1, cursor: "x" + cursor[1..]));
        Invalid(Query(paging, "Function", limit: 1, cursor: paging.Encode(100000, "other")));
        _time.Advance(TimeSpan.FromMinutes(10));
        Invalid(Query(paging, "Function", limit: 1, cursor: cursor));
    }

    [Fact]
    public void Cursor_SignedOutOfRangeOffset_IsRejected()
    {
        string query = PageCursor.Fingerprint(PropertySchemaTools.Tool, PropertyCatalog.CatalogVersion, "Function", null, null, "concise", "1");
        PropertySchemaPaging paging = Paging;
        Invalid(Query(paging, "Function", limit: 1, cursor: paging.Encode(int.MaxValue, query)));
    }

    [Fact]
    public void NameFilter_IsOrdinalIgnoreCase_AndNoMatchIsAnEmptyFinishedPage()
    {
        JsonElement data = Data(Query(Paging, "Function", name_contains: "fUnC_tExT"));
        Assert.Equal(["FUNC_TEXT", "FUNC_TEXT_AUTOMATIC", "FUNC_TEXT_COMMON"], Items(data).Select(e => e.GetProperty("property_name").GetString()));
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("cursor").ValueKind);
        Assert.Empty(Items(Data(Query(Paging, "Function", name_contains: "NO_SUCH_PROPERTY"))));
    }

    [Fact]
    public void Selector_ResolvesFormalNameOrId_SortsRegardlessOfInputAndKeepsUnknownIndexFacts()
    {
        JsonElement data = Data(Query(Paging, "Function", Json("""
            [{"property_name":"FUNC_TECHNICAL_CHARACTERISTIC","index":-2147483648},{"property_id":20011}]
            """), response_format: "detailed"));
        Assert.Equal([20011, 20027], Items(data).Select(e => e.GetProperty("property_id").GetInt32()));
        Assert.All(Items(data), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("definition").GetProperty("is_indexed").ValueKind));
    }

    public static IEnumerable<object[]> Exclusions => PropertyCatalog.Entries.Where(e => e.ReadRefusal is not null)
        .GroupBy(e => e.ReadRefusal, StringComparer.Ordinal).Select(g => new object[] { g.First().ObjectType, g.First().PropertyName, g.Key! });

    [Theory]
    [MemberData(nameof(Exclusions))]
    [InlineData("StorableObject", "PROPUSER_TEST", "user_metadata")]
    public void ExplicitSelection_ExposesTheShippedRefusalCategoryWithoutReadingValues(string type, string name, string refusal)
    {
        JsonElement selector = JsonSerializer.SerializeToElement(new[] { new { property_name = name } });
        JsonElement item = Assert.Single(Items(Data(Query(Paging, type, selector, response_format: "detailed"))));
        Assert.Equal(refusal, item.GetProperty("read").GetString());
        Assert.False(item.TryGetProperty("value", out _));
        Assert.False(item.GetProperty("write").GetProperty("write_allowed").GetBoolean());
    }

    [Fact]
    public void NameFiltering_PropUserNames_AreAllRefused()
    {
        JsonElement data = Data(Query(Paging, "Page", name_contains: "PROPUSER_"));
        Assert.NotEmpty(Items(data));
        Assert.All(Items(data), e => Assert.Equal("user_metadata", e.GetProperty("read").GetString()));
    }

    [Fact]
    public void NoPublicId_IsExplicitNull_AndSortsAfterNumericIds()
    {
        JsonElement data = Data(Query(Paging, "MDFunctionTemplatePosition", name_contains: "FUNC_", limit: 200));
        JsonElement[] rows = Items(data);
        Assert.Contains(rows, e => e.GetProperty("property_name").GetString() == "FUNC_TECHNICAL_CHARACTERISTIC"
            && e.GetProperty("property_id").ValueKind == JsonValueKind.Null && e.GetProperty("read").GetString() == "no_public_id");
        Assert.Equal(rows.OrderBy(e => e.GetProperty("property_id").ValueKind == JsonValueKind.Null ? int.MaxValue : e.GetProperty("property_id").GetInt32())
            .ThenBy(e => e.GetProperty("property_name").GetString(), StringComparer.Ordinal).Select(e => e.GetRawText()), rows.Select(e => e.GetRawText()));
    }

    [Fact]
    public void Detailed_AllUnknownFactsAreExplicitNull_ConciseOmitsOnlyDetailedFields()
    {
        JsonElement selector = Json("[{\"property_id\":20011}]");
        JsonElement item = Assert.Single(Items(Data(Query(Paging, "Function", selector, response_format: "detailed"))));
        Assert.Equal("readable", item.GetProperty("read").GetString());
        Assert.False(item.GetProperty("obsolete").GetBoolean());
        Assert.Equal(PropertyCatalog.CatalogVersion, item.GetProperty("version").GetString());
        Assert.Equal("catalog/eplan-2026.0.3/data-dictionary.jsonl", item.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("runtime_differences").ValueKind);
        Assert.Equal(17, item.GetProperty("definition").EnumerateObject().Count());
        Assert.All(item.GetProperty("definition").EnumerateObject(), fact => Assert.Equal(JsonValueKind.Null, fact.Value.ValueKind));
        Assert.All(Items(Data(Query(Paging, "Function", selector))), concise =>
            Assert.Equal(["property_id", "property_name", "read", "write"], concise.EnumerateObject().Select(p => p.Name)));
    }

    public static IEnumerable<object[]> ObjectTypes => PropertyCatalog.Entries.Select(e => e.ObjectType).Distinct(StringComparer.Ordinal).Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(ObjectTypes))]
    public void Catalog_OnlyB1ShippedWhitelist_AllowsWritesWithoutInferringApiWritability(string type)
    {
        Assert.Equal([(2026, "Function", 20011, ""), (2026, "Function", 20027, "")], PropertyWriteWhitelist.Entries.Keys.OrderBy(k => k.PropertyId));
        PropertySchemaPaging paging = Paging;
        foreach (PropertyDictionaryEntry[] batch in PropertyCatalog.Entries.Where(e => e.ObjectType == type).Chunk(200))
        {
            JsonElement selectors = JsonSerializer.SerializeToElement(batch.Select(e => new { property_name = e.PropertyName }));
            string? cursor = null;
            do
            {
                JsonElement data = Data(Query(paging, type, selectors, limit: 200, cursor: cursor));
                Assert.NotEmpty(Items(data));
                Assert.All(Items(data), item =>
                {
                    bool allowed = type == "Function" && item.GetProperty("property_id").ValueKind == JsonValueKind.Number
                        && item.GetProperty("property_id").GetInt32() is 20011 or 20027;
                    Assert.Equal(allowed, item.GetProperty("write").GetProperty("write_allowed").GetBoolean());
                    Assert.Equal(JsonValueKind.Null, item.GetProperty("write").GetProperty("api_writable").ValueKind);
                    Assert.Equal(allowed ? null : "not_in_shipped_whitelist", item.GetProperty("write").GetProperty("reason").GetString());
                });
                cursor = data.GetProperty("cursor").GetString();
            }
            while (cursor is not null);
        }
    }

    [Theory]
    [InlineData("concise", 5000)]
    [InlineData("detailed", 20000)]
    public void Budget_CountsBothJsonCopiesAndSummary_AndDoesNotDropRows(string format, int budget)
    {
        PropertySchemaPaging paging = Paging;
        CallToolResult first = Query(paging, "Function", response_format: format, limit: 200);
        JsonElement data = Data(first);
        int tokens = first.Content.Cast<TextContentBlock>().Sum(b => ResultShaper.EstimateTokens(b.Text))
            + ResultShaper.EstimateTokens(data.GetRawText());
        Assert.InRange(tokens, 1, budget);
        Assert.InRange(Items(data).Length, 1, 199);
        string cursor = data.GetProperty("cursor").GetString()!;
        JsonElement next = Data(Query(paging, "Function", response_format: format, limit: 200, cursor: cursor));
        Assert.NotEqual(Items(data)[^1].GetProperty("property_id").GetInt32(), Items(next)[0].GetProperty("property_id").GetInt32());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"property_id\":20011,\"property_name\":\"FUNC_TEXT\"}]")]
    [InlineData("[{\"property_id\":20011,\"extra\":true}]")]
    [InlineData("[{\"property_id\":20011,\"property_id\":20011}]")]
    [InlineData("[{\"property_id\":0}]")]
    [InlineData("[{\"property_id\":20011.5}]")]
    [InlineData("[{\"property_id\":2147483648}]")]
    [InlineData("[{\"property_id\":\"20011\"}]")]
    [InlineData("[{\"property_name\":\"func_text\"}]")]
    [InlineData("[{\"property_name\":\"Custom.Property\"}]")]
    [InlineData("[{\"property_id\":20011,\"index\":null}]")]
    [InlineData("[{\"property_id\":20011,\"index\":2147483648}]")]
    [InlineData("[{\"property_id\":20011},{\"property_name\":\"FUNC_TEXT\"}]")]
    public void InvalidSelectors_ReturnAllViolationsAsToolErrors(string json) => Invalid(Query(Paging, "Function", Json(json)));

    [Fact]
    public void InvalidArguments_AreReportedTogether_AndCancellationPropagates()
    {
        CallToolResult invalid = Query(Paging, "function", Json("[{}]"), " ", "verbose", 0, "!");
        Invalid(invalid);
        using JsonDocument error = JsonDocument.Parse(((TextContentBlock)invalid.Content[0]).Text);
        Assert.True(error.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n').Length >= 7);
        Invalid(Query(Paging, "Function", limit: 201));
        Invalid(Query(Paging, "Function", name_contains: new string('x', 501)));
        Invalid(Query(Paging, "Function", JsonSerializer.SerializeToElement(Enumerable.Repeat(new { property_id = 20011 }, 201))));
        Assert.Throws<OperationCanceledException>(() => PropertySchemaTools.Schema(Paging, "Function", cancellationToken: new CancellationToken(true)));
    }

    internal static JsonElement Data(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        using JsonDocument text = JsonDocument.Parse(((TextContentBlock)result.Content[1]).Text);
        Assert.True(JsonElement.DeepEquals(data, text.RootElement));
        Assert.False(data.TryGetProperty("execution", out _));
        return data;
    }

    internal static void Invalid(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        using JsonDocument error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(ErrorReason.InvalidArgument, error.RootElement.GetProperty("error").GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, error.RootElement.GetProperty("error").GetProperty("execution").ValueKind);
    }

    private static CallToolResult Query(PropertySchemaPaging paging, string object_type, JsonElement? properties = null,
        string? name_contains = null, string response_format = "concise", int limit = 50, string? cursor = null) =>
        PropertySchemaTools.Schema(paging, object_type, properties, name_contains, response_format, limit, cursor, TestContext.Current.CancellationToken);

    private static JsonElement[] Items(JsonElement data) => [.. data.GetProperty("items").EnumerateArray()];
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
