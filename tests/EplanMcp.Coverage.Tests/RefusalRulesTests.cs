using EplanMcp.Contracts;
using EplanMcp.Coverage.Mapping;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class RefusalRulesTests
{
    private const string Rule = "api:\n  - {match: 'O:N.T.Member', status: refused, sides: [write], reason: unsafe, note: crash, source: api-notes A77}\n";

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("call")]
    public void RefusedRequiresAnExplicitSideAndKeepsItsEvidence(string side)
    {
        ApiRule rule = Assert.Single(Overlay.Parse(Rule.Replace("[write]", "[" + side + "]", StringComparison.Ordinal), []).Api);
        Assert.Equal([side], rule.Sides);
        Assert.Equal("unsafe", rule.Reason);
        Assert.Equal("api-notes A77", rule.Source);
    }

    [Theory]
    [InlineData("sides: [write], ", "")]
    [InlineData("[write]", "[]")]
    [InlineData("[write]", "null")]
    [InlineData("[write]", "[read, unknown]")]
    [InlineData("reason: unsafe, ", "")]
    [InlineData("reason: unsafe", "reason: gui")]
    [InlineData("note: crash, ", "")]
    [InlineData("note: crash", "note: ''")]
    [InlineData("source: api-notes A77", "source: ' '")]
    [InlineData("source: api-notes A77", "source: null")]
    [InlineData("source: api-notes A77", "")]
    [InlineData("O:N.T.Member", "N:N.T")]
    public void IncompleteOrInvalidRefusalIsRejected(string before, string after) =>
        Assert.Contains("refused with a group ID, sides", Assert.Throws<InvalidDataException>(() => Overlay.Parse(Rule.Replace(before, after, StringComparison.Ordinal), [])).Message, StringComparison.Ordinal);

    [Theory]
    [InlineData("O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate", "0xe0434352")]
    [InlineData("O:Eplan.EplApi.DataModel.MacroBox.RepresentationType", "0xc0000409")]
    [InlineData("O:Eplan.EplApi.DataModel.Page.ConvertToGraphics", "0xc0000409")]
    public void GeneratedLookupRetainsExactLiveGroupAndWriteSide(string group, string code)
    {
        Assert.True(ApiTryCatalog.Allowed(group));
        Assert.Contains(code, ApiTryCatalog.Refused(group, "write"), StringComparison.Ordinal);
        Assert.Null(ApiTryCatalog.Refused(group, "read"));
        Assert.Null(ApiTryCatalog.Refused(group, "call"));
        Assert.Null(ApiTryCatalog.Refused(group.ToLowerInvariant(), "write"));
    }
}
