using System.Text.Json.Nodes;
using EplanMcp.Evals.Scoring;

namespace EplanMcp.Evals.Tests.Scoring;

[Trait("Size", "Small")]
public sealed class ComparisonTests
{
    [Theory]
    [InlineData("12", "12")]
    [InlineData("12", "12.0")]
    [InlineData("1.5e2", "150")]
    [InlineData("\"-FC1\"", "\"-FC1\"")]
    [InlineData("\"\\u4E2D\"", "\"中\"")]
    [InlineData("true", "true")]
    [InlineData("null", "null")]
    [InlineData("[]", "[]")]
    [InlineData("[1, \"a\"]", "[1.0, \"a\"]")]
    [InlineData("{\"a\": 1, \"b\": [2]}", "{\"b\": [2.00], \"a\": 1}")]
    public void Equal_SameValues_Pass(string actual, string expected)
    {
        Assert.Null(Fail(ComparisonKind.Equal, actual, expected));
    }

    [Theory]
    [InlineData("12", "13")]
    [InlineData("12", "\"12\"")]
    [InlineData("\"-FC1\"", "\"-fc1\"")]
    [InlineData("\"-FC1\"", "\"-FC1 \"")]
    [InlineData("true", "false")]
    [InlineData("null", "0")]
    [InlineData("null", "\"\"")]
    [InlineData("[1, 2]", "[2, 1]")]
    [InlineData("[1]", "[1, 1]")]
    [InlineData("{\"a\": 1}", "{\"a\": 1, \"b\": null}")]
    [InlineData("{\"a\": 1}", "{\"A\": 1}")]
    public void Equal_DifferentValues_Fail(string actual, string expected)
    {
        Assert.Equal($"{Show(actual)} is not {Show(expected)}", Fail(ComparisonKind.Equal, actual, expected));
    }

    [Theory]
    [InlineData("\"Siemens\"", "\"SIEMENS\"", null)]
    [InlineData("\"Siemens\"", "\"Siemens AG\"", "\"Siemens\" is not \"Siemens AG\", ignoring case")]
    [InlineData("12", "\"12\"", "12 and \"12\" are not both strings")]
    [InlineData("[\"a\"]", "[\"A\"]", "[\"a\"] and [\"A\"] are not both strings")]
    public void EqualIgnoringCase_ComparesStringsOnly(string actual, string expected, string? failure)
    {
        Assert.Equal(failure, Fail(ComparisonKind.EqualIgnoringCase, actual, expected));
    }

    [Theory]
    [InlineData("[\"/3\", \"/7\"]", "[\"/7\", \"/3\"]")]
    [InlineData("[\"/3\", \"/3\", \"/7\"]", "[\"/7\", \"/3\"]")]
    [InlineData("[1, 2.0]", "[2, 1]")]
    [InlineData("[]", "[]")]
    public void SetEqual_SameElementsInAnyOrder_Pass(string actual, string expected)
    {
        Assert.Null(Fail(ComparisonKind.SetEqual, actual, expected));
    }

    [Theory]
    [InlineData("[\"/3\"]", "[\"/3\", \"/7\"]", "missing \"/7\"")]
    [InlineData("[\"/3\", \"/9\", \"/9\"]", "[\"/3\"]", "unexpected \"/9\"")]
    [InlineData("[\"/3\", \"/9\"]", "[\"/7\", \"/7\"]", "missing \"/7\"; unexpected \"/3\", \"/9\"")]
    [InlineData("[]", "[\"/3\"]", "missing \"/3\"")]
    [InlineData("[1, 2, 3, 4, 5, 6, 7]", "[]", "unexpected 1, 2, 3, 4, 5 and 2 more")]
    [InlineData("\"/3\"", "[\"/3\"]", "\"/3\" and [\"/3\"] are not both arrays")]
    [InlineData("null", "[]", "null and [] are not both arrays")]
    public void SetEqual_OtherElements_FailWithTheDifference(string actual, string expected, string failure)
    {
        Assert.Equal(failure, Fail(ComparisonKind.SetEqual, actual, expected));
    }

    [Theory]
    [InlineData("[\"Siemens\", \"Phoenix\"]", "\"Phoenix\"", null)]
    [InlineData("[\"Siemens\", \"Phoenix\"]", "[\"Phoenix\", \"Siemens\"]", null)]
    [InlineData("[\"Siemens\"]", "[]", null)]
    [InlineData("[1.0, 2]", "1", null)]
    [InlineData("[\"Siemens\"]", "\"Phoenix\"", "missing \"Phoenix\"")]
    [InlineData("[\"Siemens\"]", "[\"Phoenix\", \"Rittal\", \"Siemens\"]", "missing \"Phoenix\", \"Rittal\"")]
    [InlineData("\"=GAA+A1-FC1\"", "\"-FC1\"", null)]
    [InlineData("\"=GAA+A1-FC1\"", "\"-fc1\"", "\"=GAA+A1-FC1\" does not contain \"-fc1\"")]
    [InlineData("12", "1", "12 is neither an array nor a string that can contain 1")]
    [InlineData("\"12\"", "1", "\"12\" is neither an array nor a string that can contain 1")]
    public void Contains_FindsElementsOrSubstrings(string actual, string expected, string? failure)
    {
        Assert.Equal(failure, Fail(ComparisonKind.Contains, actual, expected));
    }

    [Theory]
    [InlineData("at_least", "3", "3", null)]
    [InlineData("at_least", "3.5", "3", null)]
    [InlineData("at_least", "2", "3", "2 is below 3")]
    [InlineData("at_most", "3", "3.0", null)]
    [InlineData("at_most", "-1", "0", null)]
    [InlineData("at_most", "4", "3", "4 is above 3")]
    [InlineData("at_least", "\"4\"", "3", "\"4\" and 3 are not both numbers")]
    [InlineData("at_most", "[1]", "3", "[1] and 3 are not both numbers")]
    [InlineData("at_least", "1e400", "3", "1e400 and 3 are not both numbers")]
    public void AtLeastAndAtMost_CompareNumbers(string name, string actual, string expected, string? failure)
    {
        Assert.Equal(failure, Fail(Comparison.Names[name], actual, expected));
    }

    [Fact]
    public void Contains_DoesNotMoveTheExpectedValue()
    {
        JsonObject task = JsonNode.Parse("""{"expected": "Phoenix"}""")!.AsObject();

        Assert.Null(Comparison.Fail(ComparisonKind.Contains, JsonNode.Parse("""["Phoenix"]"""), task["expected"]));
        Assert.Equal("Phoenix", task["expected"]!.GetValue<string>());
    }

    [Fact]
    public void Show_KeepsChineseReadable()
    {
        Assert.Equal("\"页描述\"", Comparison.Show(JsonValue.Create("页描述")));
    }

    [Fact]
    public void Names_CoverEveryKind()
    {
        Assert.Equal(Enum.GetValues<ComparisonKind>(), Comparison.Names.Values.Order());
    }

    private static string? Fail(ComparisonKind kind, string actual, string expected) =>
        Comparison.Fail(kind, JsonNode.Parse(actual), JsonNode.Parse(expected));

    private static string Show(string json) => Comparison.Show(JsonNode.Parse(json));
}
