using System.Text.Json.Nodes;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageReviewFactsTests
{
    private static PageReviewFacts Facts() => new(JsonNode.Parse("""
        {"devices":[{"locator":{"oid":1},"tag":"=GAA+A1-2K1:13","category":"Relay","main":false},
                    {"locator":{"oid":2},"tag":"WithoutSeparator","category":"","main":true}],
         "connections":[{"locator":{"oid":3},"label":"","colour":"RD","crossSection":"2.5","potentialName":"L1","potentialType":"raw:42"}]}
        """)!.AsObject(), JsonNode.Parse("""{"locator":{"project":"Line1","page":"1"},"name":"1"}""")!.AsObject());

    [Fact]
    public void Objects_ArePartitionedByKindWithSerializedLocatorKeys()
    {
        var facts = Facts();
        Assert.Equal(["{\"oid\":1}", "{\"oid\":2}"], facts.Objects("function"));
        Assert.Equal(["{\"oid\":3}"], facts.Objects("connection"));
        Assert.Single(facts.Objects("page"));
        Assert.Empty(facts.Objects("cable"));
        Assert.Equal("1", facts.GetProperty(facts.Objects("page")[0], "page.name")!.Value.GetString());
        Assert.False(facts.Relation(facts.Objects("function")[0], "function.connections").Complete);
    }

    [Fact]
    public void EmptyValues_AreKnownAbsent_MissingObjectsUnknown_FalseAndRawTextPreserved()
    {
        var facts = Facts();
        string function = facts.Objects("function")[0], empty = facts.Objects("function")[1], connection = facts.Objects("connection")[0];
        Assert.Equal("K", facts.GetProperty(function, "function.letters")!.Value.GetString());
        Assert.Equal("Relay", facts.GetProperty(function, "function.category")!.Value.GetString());
        Assert.False(facts.GetProperty(function, "function.main")!.Value.GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, facts.GetProperty(empty, "function.category")!.Value.ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, facts.GetProperty(empty, "function.letters")!.Value.ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, facts.GetProperty(connection, "connection.label")!.Value.ValueKind);
        Assert.Null(facts.GetProperty(connection, "function.name"));
        Assert.Null(facts.GetProperty("missing", "function.name"));
        foreach ((string property, string expected) in new[] { ("colour", "RD"), ("cross_section", "2.5"), ("potential_name", "L1"), ("potential_type", "raw:42") })
        { Assert.Equal(expected, facts.GetProperty(connection, "connection." + property)!.Value.GetString()); }
    }

    [Fact]
    public async Task ReadButEmptyValues_FailExists_AndStayUnknownInComparisons()
    {
        PageReviewFacts facts = Facts();
        var (scope, check) = RuleValidator.Validate(System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            severity = "mandatory",
            source = "test",
            scope = new { kind = "connection", where = new { literal = true } },
            check = new { exists = new[] { new { property = "connection.label" } } },
            message = new { en = "Label is required" }
        }));
        var (_, comparison) = RuleValidator.Validate(System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            severity = "mandatory",
            source = "test",
            scope = new { kind = "connection", where = new { literal = true } },
            check = new { eq = new object[] { new { property = "connection.label" }, new { literal = "A" } } },
            message = new { en = "Label must be A" }
        }));
        IReadOnlyList<RuleResult> results = await RuleInterpreters.Untimed().EvaluateAsync([new("exists", "1.0.0", "connection", scope, check), new("compare", "1.0.0", "connection", scope, comparison)], facts,
            new Dictionary<string, RuleValue>(), new Dictionary<string, RuleValue>(), TestContext.Current.CancellationToken);
        Assert.Equal([RuleStatus.Fail, RuleStatus.Unknown], results.Select(r => r.Status));
    }
}
