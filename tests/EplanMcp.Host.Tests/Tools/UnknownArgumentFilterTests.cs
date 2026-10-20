using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class UnknownArgumentFilterTests
{
    private static readonly JsonElement _schema = JsonDocument.Parse(
        """{"type":"object","properties":{"page":{"type":"string"},"limit":{"type":"integer"}},"required":["page"],"additionalProperties":false}""").RootElement;

    [Fact]
    public void KnownArgumentsWithTheRequiredOne_AreFine()
    {
        Assert.Empty(UnknownArgumentFilter.Violations(_schema, Arguments("""{"page":"=A1/1","limit":5}""")));
    }

    [Fact]
    public void MissingOrNullRequiredArgument_IsReported()
    {
        Assert.Equal(["Missing required argument 'page'."], UnknownArgumentFilter.Violations(_schema, null));
        Assert.Equal(["Missing required argument 'page'."], UnknownArgumentFilter.Violations(_schema, Arguments("""{"limit":5}""")));
        Assert.Equal(["Missing required argument 'page'."], UnknownArgumentFilter.Violations(_schema, Arguments("""{"page":null}""")));
    }

    [Fact]
    public void UnknownAndMissing_AreReportedTogether()
    {
        List<string> violations = UnknownArgumentFilter.Violations(_schema, Arguments("""{"pgae":"=A1/1"}"""));

        Assert.Equal(2, violations.Count);
        Assert.Contains("'pgae'", violations[0], StringComparison.Ordinal);
        Assert.Contains("valid arguments: limit, page", violations[0], StringComparison.Ordinal);
        Assert.Equal("Missing required argument 'page'.", violations[1]);
    }

    [Fact]
    public void ToolWithoutArguments_AcceptsNoneAndRejectsAny()
    {
        JsonElement none = JsonDocument.Parse("""{"type":"object","additionalProperties":false}""").RootElement;

        Assert.Empty(UnknownArgumentFilter.Violations(none, null));
        Assert.Contains("valid arguments: none", Assert.Single(UnknownArgumentFilter.Violations(none, Arguments("""{"x":1}"""))), StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectsInAnArrayArgument_AreCheckedTooWithTheirIndex()
    {
        JsonElement schema = JsonDocument.Parse(
            """{"type":"object","properties":{"pages":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"page_type":{"type":"string"}},"required":["name"]}},"tags":{"type":"array","items":{"type":"string"}}},"additionalProperties":false}""").RootElement;

        List<string> violations = UnknownArgumentFilter.Violations(schema, Arguments("""{"pages":[{"name":"=A/1"},{"name":"=A/2","type":"Overview"},{"page_type":"Graphics"},"x"],"tags":["a"]}"""));

        Assert.Equal(2, violations.Count);
        Assert.Equal("Unknown argument 'pages[1].type'; valid arguments: name, page_type.", violations[0]);
        Assert.Equal("Missing required argument 'pages[2].name'.", violations[1]);
    }

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
