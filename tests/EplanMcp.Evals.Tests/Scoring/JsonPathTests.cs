using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Tests.Scoring;

[Trait("Size", "Small")]
public sealed class JsonPathTests
{
    private static readonly JsonNode _device = JsonNode.Parse("""
        {
          "tag": "=GAA+A1-FC1",
          "description": null,
          "functions": [
            { "name": "-FC1", "page": "=GAA+A1&EFS1/3" },
            { "name": "-FC1", "page": "=GAA+A1&EFS1/7" },
            { "name": "-FC1" }
          ],
          "matrix": [[1, 2], [3]]
        }
        """)!;

    [Theory]
    [InlineData("$")]
    [InlineData("$.tag")]
    [InlineData("$.functions[0].page")]
    [InlineData("$.functions[*].page")]
    [InlineData("$.matrix[*][*]")]
    [InlineData("$[*]")]
    [InlineData("$.a_b.C9")]
    public void TryParse_TheSupportedSubset_Succeeds(string text)
    {
        Assert.True(JsonPath.TryParse(text, out JsonPath? path));
        Assert.Equal(text, path.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tag")]
    [InlineData("$.")]
    [InlineData("$tag")]
    [InlineData("$..tag")]
    [InlineData("$['tag']")]
    [InlineData("$.functions[-1]")]
    [InlineData("$.functions[0:2]")]
    [InlineData("$.functions[?(@.page)]")]
    [InlineData("$.functions[1234567890]")]
    [InlineData("$.9tag")]
    [InlineData("$.tag ")]
    [InlineData("$.tag\n")]
    public void TryParse_AnythingElse_Fails(string text)
    {
        Assert.False(JsonPath.TryParse(text, out _));
    }

    [Theory]
    [InlineData("$.tag", "\"=GAA+A1-FC1\"")]
    [InlineData("$.functions[1].page", "\"=GAA+A1&EFS1/7\"")]
    [InlineData("$.matrix[0][1]", "2")]
    public void TrySelect_ADefinitePath_SelectsTheValue(string text, string expected)
    {
        Assert.True(Path(text).TrySelect(_device, out JsonNode? value));
        Assert.Equal(expected, Comparison.Show(value));
    }

    [Fact]
    public void TrySelect_Root_SelectsTheWholeDocument()
    {
        Assert.True(Path("$").TrySelect(_device, out JsonNode? value));
        Assert.Same(_device, value);
    }

    [Fact]
    public void TrySelect_ANullThatIsThere_IsAMatch()
    {
        Assert.True(Path("$.description").TrySelect(_device, out JsonNode? value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("$.colour")]
    [InlineData("$.functions[3]")]
    [InlineData("$.functions[2].page")]
    [InlineData("$.tag.name")]
    [InlineData("$.tag[0]")]
    [InlineData("$.functions.page")]
    public void TrySelect_ADefinitePathThatMatchesNothing_Fails(string text)
    {
        Assert.False(Path(text).TrySelect(_device, out JsonNode? value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("$.functions[*].page", """["=GAA+A1&EFS1/3","=GAA+A1&EFS1/7"]""")]
    [InlineData("$.matrix[*][*]", "[1,2,3]")]
    [InlineData("$.matrix[*][1]", "[2]")]
    [InlineData("$.functions[*].colour", "[]")]
    [InlineData("$.tag[*]", "[]")]
    public void TrySelect_AWildcardPath_SelectsTheArrayOfMatches(string text, string expected)
    {
        Assert.True(Path(text).TrySelect(_device, out JsonNode? value));
        Assert.Equal(expected, Comparison.Show(value));
    }

    [Fact]
    public void TrySelect_AWildcardPath_LeavesTheSourceIntact()
    {
        Assert.True(Path("$.functions[*]").TrySelect(_device, out JsonNode? value));

        Assert.Equal(3, value!.AsArray().Count);
        Assert.Equal(3, _device["functions"]!.AsArray().Count);
    }

    [Fact]
    public void TrySelect_FromNull_MatchesOnlyTheRoot()
    {
        Assert.True(Path("$").TrySelect(null, out _));
        Assert.False(Path("$.tag").TrySelect(null, out _));
    }

    [Theory]
    [InlineData("$.functions[0].page", "functions")]
    [InlineData("$", null)]
    [InlineData("$[0].tag", null)]
    public void FirstField_IsTheLeadingFieldName(string text, string? expected)
    {
        Assert.Equal(expected, Path(text).FirstField);
    }

    private static JsonPath Path(string text) => JsonPath.TryParse(text, out JsonPath? path) ? path : throw new ArgumentException(text);
}
