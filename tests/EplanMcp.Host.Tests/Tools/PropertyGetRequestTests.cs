using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PropertyGetRequestTests
{
    internal const string Project = "X:/fixture/Line1";

    [Fact]
    public void Host_AssemblyReferences_KeepTheBridgeAndItsSerializerOut()
    {
        AssemblyName[] references = typeof(PropertyGetRequest).Assembly.GetReferencedAssemblies();
        Assert.Contains(references, r => r.Name == "EplanMcp.Contracts");
        Assert.DoesNotContain(references, r => r.Name == "EplanMcp.Bridge.Core");
        Assert.DoesNotContain(references, r => r.Name == "Newtonsoft.Json");
    }

    [Theory]
    [InlineData("{\"project_object\":true}", "PROJ_DRAWINGNUMBER", 10013)]
    [InlineData("{\"page\":\"=AP+ST1/3\"}", "PAGE_NOMINATIOMN", 11011)]
    [InlineData("{\"device_tag\":\"=AP+ST1-K1\",\"page\":\"=AP+ST1/3\"}", "FUNC_TEXT", 20011)]
    [InlineData("{\"locator\":{\"project\":\"X:/fixture/Line1\",\"kind\":\"page\",\"type\":17,\"oid\":42}}", "PAGE_NOMINATIOMN", 11011)]
    [InlineData("{\"locator\":{\"project\":\"X:/fixture/Line1\",\"kind\":\"function\",\"type\":17,\"oid\":42}}", "FUNC_TEXT", 20011)]
    [InlineData("{\"locator\":{\"project\":\"X:/fixture/Line1\",\"kind\":\"connection\",\"type\":17,\"oid\":42}}", "CONNECTION_WIRECROSSSECTION", 31002)]
    public void NamesAndIds_EachTargetBranch_ResolveWithinItsDictionaryType(string target, string name, int id)
    {
        foreach (string selector in new[] { "{\"property_name\":\"" + name + "\"}", "{\"property_id\":" + id.ToString(CultureInfo.InvariantCulture) + "}" })
        {
            string json = Query(target, selector);
            Assert.Null(PropertyGetRequest.Validate(json, out Dictionary<(int Query, int Selector), int> properties));
            Assert.Equal(id, Assert.Single(properties).Value);
            Assert.Equal((0, 0), Assert.Single(properties).Key);
        }
    }

    [Fact]
    public void MixedQueries_ResolvedIds_KeepOriginalQueryAndSelectorPositions()
    {
        const string Json = """
            {"queries":[
              {"target":{"project_object":true},"properties":[{"property_name":"PROJ_DRAWINGNUMBER"}]},
              {"target":{"page":"P"},"properties":[{"property_name":"PAGE_NOMINATIOMN"}]},
              {"target":{"device_tag":"K1"},"properties":[{"property_name":"FUNC_TEXT"},{"property_id":20027}]},
              {"target":{"locator":{"project":"X:/fixture/Line1","kind":"connection","type":21,"oid":42}},"properties":[{"property_name":"CONNECTION_WIRECROSSSECTION"}]}],
              "language":"zh_CN","response_format":"detailed","limit":200,"cursor":"opaque"}
            """;
        Assert.Null(PropertyGetRequest.Validate(Json, out Dictionary<(int Query, int Selector), int> properties));
        Assert.Equal(5, properties.Count);
        Assert.Equal(10013, properties[(0, 0)]);
        Assert.Equal(11011, properties[(1, 0)]);
        Assert.Equal(20011, properties[(2, 0)]);
        Assert.Equal(20027, properties[(2, 1)]);
        Assert.Equal(31002, properties[(3, 0)]);
    }

    [Fact]
    public void WholeRequest_AllHostViolations_ArePointersAndNoResolutionTableIsPublished()
    {
        const string Json = """
            {"project":false,"language":"english","response_format":"verbose","limit":"50","cursor":null,"a~/":true,
              "queries":[
                {"target":{"project_object":true},"properties":[{"property_id":10013}]},
                {"target":{"device_tag":"K1"},"extra":true,"properties":[{"property_name":"func_text"}]},
                {"target":{"page":"P"},"properties":[]},
                null]}
            """;
        PropertyViolation[] violations = Violations(Failure(Json));
        string[] expected = ["/project", "/language", "/response_format", "/limit", "/cursor", "/a~0~1", "/queries/1/extra",
            "/queries/1/properties/0/property_name", "/queries/2/properties", "/queries/3"];
        Assert.All(expected, path => Assert.Contains(violations, v => v.Path == path));
        Assert.All(violations, v => Assert.Equal(ErrorReason.InvalidArgument, v.Reason));
    }

    [Theory]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_name\":\"func_text\"}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_name\":\"Function text\"}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_name\":\"Custom.Property\"}")]
    [InlineData("{\"page\":\"P\"}", "{\"property_name\":\"FUNC_TEXT\"}")]
    public void FormalName_UnknownOrOnAnotherObjectType_IsAnArgumentError(string target, string selector)
    {
        Assert.Contains(Violations(Failure(Query(target, selector))), v => v.Path == "/queries/0/properties/0/property_name");
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("{\"project_object\":false}", "{\"property_id\":10013}")]
    [InlineData("{\"project_object\":true,\"page\":\"P\"}", "{\"property_id\":10013}")]
    [InlineData("{\"project_object\":true,\"device_tag\":\"K1\"}", "{\"property_name\":\"FUNC_TEXT\"}")]
    [InlineData("{\"page\":true}", "{\"property_id\":11011}")]
    [InlineData("{\"device_tag\":\"K1\",\"extra\":true}", "{\"property_id\":20011,\"extra\":true}")]
    [InlineData("{\"locator\":[]}", "[]")]
    [InlineData("{\"locator\":{\"kind\":\"connection\",\"project\":\"Other\",\"type\":0,\"oid\":false,\"pos\":\"NaN,0\"}}", "{\"property_id\":31002}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_id\":\"20011\"}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_id\":20011.0}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_id\":20011,\"property_name\":\"FUNC_TEXT\"}")]
    [InlineData("{\"device_tag\":\"K1\"}", "{\"property_id\":20011,\"index\":null}")]
    public void TargetAndSelectorShapes_AreLeftForTheBridge(string target, string selector)
    {
        Assert.Null(PropertyGetRequest.Validate(Query(target, selector), out _));
    }

    [Theory]
    [InlineData("{\"property_id\":2000}", 2000)]
    [InlineData("{\"property_id\":3010}", 3010)]
    [InlineData("{\"property_name\":\"PROPUSER_LAST_USEREMAIL\"}", 3013)]
    [InlineData("{\"property_id\":999999}", 999999)]
    public void NumericIdsAndExcludedNames_LeaveReadClassificationToTheBridge(string selector, int id)
    {
        Assert.Null(PropertyGetRequest.Validate(Query("{\"project_object\":true}", selector), out Dictionary<(int Query, int Selector), int> properties));
        Assert.Equal(id, Assert.Single(properties).Value);
    }

    [Theory]
    [InlineData("{}", "/queries")]
    [InlineData("[]", "")]
    [InlineData("null", "")]
    [InlineData("{\"queries\":null}", "/queries")]
    [InlineData("{\"queries\":[]}", "/queries")]
    [InlineData("{\"queries\":[{}]}", "/queries/0/properties")]
    [InlineData("{\"queries\":[{\"properties\":[]}]}", "/queries/0/properties")]
    [InlineData("{\"queries\":[],\"queries\":[]}", "/queries")]
    [InlineData("{\"queries\":[{\"target\":{\"device_tag\":\"K1\"},\"properties\":[{\"property_id\":20011,\"property_id\":20011}]}]}", "/queries/0/properties/0/property_id")]
    public void MissingBatchShapeAndDuplicateFields_AreReportedAtOriginalPointers(string json, string path)
    {
        Assert.Contains(Violations(Failure(json)), v => v.Path == path);
    }

    [Theory]
    [InlineData(50, 4, true, "")]
    [InlineData(10, 20, true, "")]
    [InlineData(51, 1, false, "/queries")]
    [InlineData(1, 21, false, "/queries/0/properties")]
    [InlineData(11, 20, false, "/queries")]
    public void RequestLimits_CountAllOriginalSelectors(int queryCount, int selectorCount, bool valid, string path)
    {
        string json = JsonSerializer.Serialize(new
        {
            queries = Enumerable.Range(0, queryCount).Select(i => new
            {
                target = new
                {
                    device_tag = "K" + i.ToString(CultureInfo.InvariantCulture),
                },
                properties = Enumerable.Range(0, selectorCount).Select(index => new
                {
                    property_id = 20011,
                    index,
                }),
            }),
        });
        if (valid)
        {
            Assert.Null(PropertyGetRequest.Validate(json, out Dictionary<(int Query, int Selector), int> properties));
            Assert.Equal(queryCount * selectorCount, properties.Count);
        }
        else
        {
            Assert.Contains(Violations(Failure(json)), v => v.Path == path);
        }
    }

    [Theory]
    [InlineData("\"limit\":null", "/limit")]
    [InlineData("\"limit\":0", "/limit")]
    [InlineData("\"limit\":201", "/limit")]
    [InlineData("\"limit\":50.0", "/limit")]
    [InlineData("\"limit\":99999999999999999999999999999999999999", "/limit")]
    [InlineData("\"cursor\":false", "/cursor")]
    [InlineData("\"cursor\":\" \"", "/cursor")]
    [InlineData("\"response_format\":null", "/response_format")]
    [InlineData("\"response_format\":\"verbose\"", "/response_format")]
    [InlineData("\"language\":\"en_US\\n\"", "/language")]
    [InlineData("\"language\":\"ZH_cn\"", "/language")]
    public void InvalidOptions_AreReportedAlongsideBatchErrors(string field, string path)
    {
        Assert.Contains(Violations(Failure("{\"queries\":[]," + field + "}")), v => v.Path == path);
    }

    [Fact]
    public void DuplicateTargetKeys_ResolveAliasesAndIgnoreObjectFieldOrder()
    {
        const string Json = """
            {"queries":[
              {"target":{"device_tag":"K1","page":"P"},"properties":[{"property_id":20011,"index":2}]},
              {"target":{"page":"P","device_tag":"K1"},"properties":[{"property_name":"FUNC_TEXT","index":2}]}]}
            """;
        Assert.Contains(Violations(Failure(Json)), v => v.Path == "/queries/1/properties/0");
    }

    [Fact]
    public void LocatorDuplicateKeys_UseIdentityRatherThanOptionalChecks()
    {
        const string Json = """
            {"queries":[
              {"target":{"locator":{"project":"X:/fixture/Line1","kind":"function","type":17,"oid":42,"name":"Old"}},"properties":[{"property_id":20011}]},
              {"target":{"locator":{"oid":42,"type":17,"kind":"function","project":"X:/fixture/Line1","name":"New"}},"properties":[{"property_name":"FUNC_TEXT"}]}]}
            """;
        Assert.Contains(Violations(Failure(Json)), v => v.Path == "/queries/1/properties/0");
    }

    [Fact]
    public void UnresolvedAliasesAndInvalidLocatorIdentities_AreNotGuessedToBeDuplicates()
    {
        const string Json = """
            {"queries":[
              {"target":{"device_tag":"K1"},"properties":[{"property_id":20011}]},
              {"target":{"device_tag":"K1","page":"P"},"properties":[{"property_id":20011}]},
              {"target":{"locator":{"kind":"connection","type":0,"oid":1}},"properties":[{"property_id":31002}]},
              {"target":{"locator":{"kind":"connection","type":0,"oid":2}},"properties":[{"property_id":31002}]}]}
            """;
        Assert.Null(PropertyGetRequest.Validate(Json, out _));
    }

    [Fact]
    public void InputBudget_CountsOriginalUtf8AndWhitespaceAtTheBoundary()
    {
        string original = Query("{\"device_tag\":\"K1\"}", "{\"property_id\":20011}");
        string boundary = original + new string(' ', 65536 - Encoding.UTF8.GetByteCount(original));
        Assert.Null(PropertyGetRequest.Validate(boundary, out _));
        Assert.Contains(Violations(Failure(boundary + " ")), v => v.Path == string.Empty);
        string utf8 = "{\"queries\":[],\"cursor\":\"" + new string('é', 33000) + "\"}";
        PropertyViolation[] violations = Violations(Failure(utf8));
        Assert.Contains(violations, v => v.Path == string.Empty);
        Assert.Contains(violations, v => v.Path == "/queries");
    }

    private static string Query(string target, string selector) => "{\"queries\":[{\"target\":" + target + ",\"properties\":[" + selector + "]}]}";

    internal static JsonElement Failure(string json)
    {
        CallToolResult result = Assert.IsType<CallToolResult>(PropertyGetRequest.Validate(json, out Dictionary<(int Query, int Selector), int> properties));
        Assert.Empty(properties);
        JsonElement error = Error(result);
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
        return error;
    }

    internal static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        using JsonDocument document = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        JsonElement error = document.RootElement.GetProperty("error");
        Assert.Equal(JsonValueKind.Null, error.GetProperty("execution").ValueKind);
        return error.Clone();
    }

    internal static PropertyViolation[] Violations(JsonElement error) => JsonSerializer.Deserialize<PropertyViolation[]>(
        error.GetProperty("metadata").GetProperty("violations").GetString()!, PropertySchemaTools.MetadataJson)!;
}
