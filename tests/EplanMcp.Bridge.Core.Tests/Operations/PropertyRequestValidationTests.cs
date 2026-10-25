using System.Globalization;
using System.Text;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyRequestValidationTests
{
    private static JArray Queries(int queries, int selectors) => new(Enumerable.Range(0, queries)
        .Select(_ => new JObject { ["properties"] = new JArray(Enumerable.Repeat(new JObject { ["property_id"] = 20011 }, selectors)) }));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 20)]
    [InlineData(50, 4)]
    public void Queries_InclusiveBudgets_Accepts(int queries, int selectors)
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(Queries(queries, selectors), violations);
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("null", "/queries")]
    [InlineData("{}", "/queries")]
    [InlineData("[]", "/queries")]
    [InlineData("[null]", "/queries/0")]
    [InlineData("[[]]", "/queries/0")]
    [InlineData("[{}]", "/queries/0/properties")]
    [InlineData("[{\"properties\":null}]", "/queries/0/properties")]
    [InlineData("[{\"properties\":{}}]", "/queries/0/properties")]
    [InlineData("[{\"properties\":[]}]", "/queries/0/properties")]
    public void Queries_InvalidArrayOrMissingSelectors_ReportsExactPointer(string json, string path)
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(JToken.Parse(json), violations);
        Assert.Equal(path, Assert.Single(violations).Path);
    }

    [Fact]
    public void Queries_MissingArray_IsRejected()
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(null, violations);
        Assert.Equal("/queries", Assert.Single(violations).Path);
    }

    [Theory]
    [InlineData(51, 1, "/queries")]
    [InlineData(1, 21, "/queries/0/properties")]
    [InlineData(11, 20, "/queries")]
    public void Queries_EachIndependentLimit_IsRejected(int queries, int selectors, string path)
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(Queries(queries, selectors), violations);
        Assert.Equal(path, Assert.Single(violations).Path);
    }

    [Fact]
    public void Queries_TwoHundredAndOneRequests_AreRejected()
    {
        JArray queries = Queries(10, 20);
        queries.Add(Queries(1, 1)[0]);
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(queries, violations);
        Assert.Equal("/queries", Assert.Single(violations).Path);
    }

    [Fact]
    public void Queries_OverLimitArraysAndInvalidRows_CollectsAllAndCountsInvalidSelectors()
    {
        JArray queries = Queries(51, 21);
        queries[0] = JValue.CreateNull();
        queries[1] = new JObject();
        queries[2] = new JObject { ["properties"] = new JArray() };
        queries[3]!["properties"]![0] = null;
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Queries(queries, violations);
        Assert.Equal(53, violations.Count);
        Assert.Equal(2, violations.Count(v => v.Path == "/queries"));
        Assert.Contains(violations, v => v.Path == "/queries/0");
        Assert.Contains(violations, v => v.Path == "/queries/1/properties");
        Assert.Contains(violations, v => v.Path == "/queries/50/properties");
        Assert.All(violations, v => Assert.Equal(ErrorReason.InvalidArgument, v.Reason));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void Changes_OriginalArrayCount_IncludesInvalidItems(int count, bool valid)
    {
        var changes = new JArray(Enumerable.Repeat(JValue.CreateNull(), count));
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Changes(changes, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal("/changes", v.Path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"changes\"")]
    public void Changes_NotAnArray_IsRejected(string json)
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Changes(JToken.Parse(json), violations);
        Assert.Equal("/changes", Assert.Single(violations).Path);
    }

    [Fact]
    public void Changes_MissingArray_IsRejected()
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Changes(null, violations);
        Assert.Equal("/changes", Assert.Single(violations).Path);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void ResolvedChanges_ObjectLimit_UsesDistinctResolvedIdentities(int count, bool valid)
    {
        (string, int, int?, string?)?[] changes = Enumerable.Range(0, count)
            .Select(i => ((string, int, int?, string?)?)(i.ToString(CultureInfo.InvariantCulture), 20011, null, "en_US")).ToArray();
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.ResolvedChanges(changes, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal("/changes", v.Path));
    }

    [Fact]
    public void ResolvedChanges_TwoAssignmentsPerObject_CountsFiftyObjectsOnly()
    {
        (string, int, int?, string?)?[] changes = Enumerable.Range(0, 100)
            .Select(i => ((string, int, int?, string?)?)((i / 2).ToString(CultureInfo.InvariantCulture), 20011 + i % 2, null, "en_US")).ToArray();
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.ResolvedChanges(changes, violations);
        Assert.Empty(violations);
    }

    [Fact]
    public void ResolvedChanges_SameTargetIdIndexLanguage_ReportsEveryLaterDuplicateAtOriginalIndex()
    {
        // Business-name and locator aliases have already resolved to the same project-local identity.
        (string, int, int?, string?)?[] changes =
        [
            ("function:17:42", 20011, 2, "en_US"),
            null,
            ("function:17:42", 20011, 2, "en_US"),
            ("function:17:42", 20011, 2, "en_US"),
        ];
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.ResolvedChanges(changes, violations);
        string[] expected = ["/changes/2", "/changes/3"];
        Assert.Equal(expected, violations.Select(v => v.Path));
    }

    [Fact]
    public void ResolvedChanges_AnyDifferentKeyComponent_IsNotDuplicate()
    {
        (string, int, int?, string?)?[] changes =
        [
            ("function:17:42", 20011, null, null),
            ("function:17:43", 20011, null, null),
            ("function:17:42", 20027, null, null),
            ("function:17:42", 20011, 0, null),
            ("function:17:42", 20011, null, "en_US"),
            ("function:17:42", 20011, null, "de_DE"),
        ];
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.ResolvedChanges(changes, violations);
        Assert.Empty(violations);
    }

    [Fact]
    public void ResolvedChanges_FormalNameAndId_UseDictionaryResolvedIdForDuplicates()
    {
        var violations = new List<PropertyViolation>();
        PropertyDictionaryEntry byName = PropertyAddressValidation.Selector(JObject.Parse("{\"property_name\":\"FUNC_TEXT\"}"), "Function", "/changes/0/property", violations)!;
        PropertyDictionaryEntry byId = PropertyAddressValidation.Selector(JObject.Parse("{\"property_id\":20011}"), "Function", "/changes/1/property", violations)!;
        PropertyRequestValidation.ResolvedChanges(
            [("function:17:42", byName.PropertyId!.Value, null, "en_US"), ("function:17:42", byId.PropertyId!.Value, null, "en_US")], violations);
        Assert.Equal("/changes/1", Assert.Single(violations).Path);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public void Input_CompleteOriginalJsonIncludingWhitespace_EnforcesExactByteBoundary(int length, bool valid)
    {
        string original = "{}" + new string(' ', length - 2);
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Input(original, violations);
        Assert.Equal(valid ? 0 : 1, violations.Count);
        Assert.All(violations, v => Assert.Equal("", v.Path));
    }

    [Fact]
    public void Input_MultibyteText_MeasuresUtf8InsteadOfCharactersOrReserializedJson()
    {
        string original = "{\"project\":\"" + new string('字', 21840) + "\"}";
        int padding = 65536 - Encoding.UTF8.GetByteCount(original);
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Input(original + new string(' ', padding), violations);
        Assert.Empty(violations);
        PropertyRequestValidation.Input(original + new string(' ', padding + 1), violations);
        Assert.Equal("", Assert.Single(violations).Path);
    }

    [Fact]
    public void AllChecks_IndependentErrors_AppendWithoutShortCircuiting()
    {
        var violations = new List<PropertyViolation>();
        PropertyRequestValidation.Input("{}" + new string(' ', 65536), violations);
        PropertyRequestValidation.Queries(Queries(1, 0), violations);
        PropertyRequestValidation.Changes(new JArray(Enumerable.Repeat(new JObject(), 101)), violations);
        (string, int, int?, string?)?[] changes = Enumerable.Range(0, 51)
            .Select(i => ((string, int, int?, string?)?)(i.ToString(CultureInfo.InvariantCulture), 20011, null, null))
            .Append(("0", 20011, null, null)).ToArray();
        PropertyRequestValidation.ResolvedChanges(changes, violations);
        PropertyAddressValidation.Target(new JObject(), "Line1", "/changes/0/target", violations);
        string[] expected = ["", "/queries/0/properties", "/changes", "/changes/51", "/changes", "/changes/0/target"];
        Assert.Equal(expected, violations.Select(v => v.Path));
        Assert.All(violations, v => Assert.Equal(ErrorReason.InvalidArgument, v.Reason));
    }
}
