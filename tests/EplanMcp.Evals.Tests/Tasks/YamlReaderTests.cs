using EplanMcp.Evals.Scoring;
using EplanMcp.Evals.Tasks;
using YamlDotNet.RepresentationModel;

namespace EplanMcp.Evals.Tests.Tasks;

[Trait("Size", "Small")]
public sealed class YamlReaderTests
{
    private readonly YamlReader _reader = new();

    [Theory]
    [InlineData("12", "12")]
    [InlineData("-1.5e3", "-1.5e3")]
    [InlineData("'12'", "\"12\"")]
    [InlineData("\"12\"", "\"12\"")]
    [InlineData("007", "\"007\"")]
    [InlineData("1.", "\"1.\"")]
    [InlineData("0x1F", "\"0x1F\"")]
    [InlineData("true", "true")]
    [InlineData("False", "false")]
    [InlineData("'true'", "\"true\"")]
    [InlineData("yes", "\"yes\"")]
    [InlineData("~", "null")]
    [InlineData("null", "null")]
    [InlineData("{ a: }", "{\"a\":null}")]
    [InlineData("'null'", "\"null\"")]
    [InlineData("=GAA+A1-FC1", "\"=GAA+A1-FC1\"")]
    [InlineData("|\n  两行\n  文字", "\"两行\\n文字\"")]
    [InlineData("[a, 2, { b: c }]", "[\"a\",2,{\"b\":\"c\"}]")]
    public void Json_FollowsPlainScalarTyping(string yaml, string json)
    {
        Assert.Equal(json, Comparison.Show(_reader.Json(Value(yaml))));
        Assert.Empty(_reader.Problems);
    }

    [Fact]
    public void Json_ARepeatedAlias_IsCopiedEachTime()
    {
        Assert.Equal("[[1,2],[1,2]]", Comparison.Show(_reader.Json(Value("[&a [1, 2], *a]"))));
        Assert.Empty(_reader.Problems);
    }

    [Theory]
    [InlineData("!!str 12", "line 1: YAML tags such as tag:yaml.org,2002:str are not supported; quote the value instead")]
    [InlineData("{ ? [k] : 1 }", "line 1: keys must be strings")]
    [InlineData("&x { b: *x }", "line 1: an alias refers to a node that contains it")]
    [InlineData("&x [1, [2, *x]]", "line 1: an alias refers to a node that contains it")]
    public void Json_WhatHasNoJsonForm_IsReported(string yaml, string problem)
    {
        _reader.Json(Value(yaml));

        Assert.Equal([problem], _reader.Problems);
    }

    [Fact]
    public void Json_AliasesThatExpandWithoutBound_AreReportedOnce()
    {
        YamlNode node = Value("""
            - &a [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
            - &b [*a, *a, *a, *a, *a, *a, *a, *a, *a, *a]
            - &c [*b, *b, *b, *b, *b, *b, *b, *b, *b, *b]
            - [*c, *c, *c, *c, *c, *c, *c, *c, *c, *c, *c]
            """);

        _reader.Json(node);

        // Reported where the limit is reached, which may be inside an aliased node.
        Assert.Single(_reader.Problems);
        Assert.EndsWith(": the file expands to more than 10000 values", _reader.Problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Accessors_ReportWithTheLineAndReturnNull()
    {
        var map = (YamlMappingNode)Value("""
            id: t
            set: [dev]
            prompt: "  "
            ? [x]
            : 1
            """);

        Assert.Equal("t", _reader.Text(_reader.Required(map, "id"), "'id'"));
        Assert.Null(_reader.Text(_reader.Required(map, "set"), "'set'"));
        Assert.Null(_reader.Text(_reader.Required(map, "prompt"), "'prompt'"));
        Assert.Null(_reader.Required(map, "answer"));
        Assert.Null(_reader.Mapping(YamlReader.Child(map, "id")!, "'id'"));
        _reader.Unknown(map, ["id", "set", "prompt"]);

        Assert.Equal(
            [
                "line 2: 'set' must be a non-empty string",
                "line 3: 'prompt' must be a non-empty string",
                "line 1: 'answer' is missing",
                "line 1: 'id' must be a mapping",
                "line 4: unknown key (not a string); expected id, set, prompt",
            ],
            _reader.Problems);
    }

    private static YamlNode Value(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents[0].RootNode;
    }
}
