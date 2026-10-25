using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PropertyAddressValidationTests
{
    private const string Project = "X:/fixture/Line1";
    private const string At = "/queries/0/properties/0";

    [Theory]
    [InlineData("{\"property_id\":20011}")]
    [InlineData("{\"property_name\":\"FUNC_TEXT\"}")]
    [InlineData("{\"property_name\":\"FUNC_TEXT\",\"index\":-2147483648}")]
    [InlineData("{\"property_id\":20011,\"index\":2147483647}")]
    public void Selector_ExactIdOrFormalName_ResolvesOnItsOwnType(string json)
    {
        var violations = new List<PropertyViolation>();
        PropertyDictionaryEntry? entry = PropertyAddressValidation.Selector(JToken.Parse(json), "Function", At, violations);
        Assert.Empty(violations);
        Assert.Equal(20011, entry!.PropertyId);
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("null", "")]
    [InlineData("[]", "")]
    [InlineData("1", "")]
    [InlineData("{\"property_id\":null}", "/property_id")]
    [InlineData("{\"property_id\":\"20011\"}", "/property_id")]
    [InlineData("{\"property_id\":true}", "/property_id")]
    [InlineData("{\"property_id\":20011.0}", "/property_id")]
    [InlineData("{\"property_id\":-1}", "/property_id")]
    [InlineData("{\"property_id\":2147483648}", "/property_id")]
    [InlineData("{\"property_id\":9999999999999999999999999}", "/property_id")]
    [InlineData("{\"property_name\":null}", "/property_name")]
    [InlineData("{\"property_name\":false}", "/property_name")]
    [InlineData("{\"property_name\":\" \"}", "/property_name")]
    [InlineData("{\"property_name\":\"功能文本\"}", "/property_name")]
    [InlineData("{\"property_name\":\"Function text\"}", "/property_name")]
    [InlineData("{\"property_name\":\"FUNC_TEXT*\"}", "/property_name")]
    [InlineData("{\"property_name\":\"func_text\"}", "/property_name")]
    [InlineData("{\"property_name\":\"Custom.Property\"}", "/property_name")]
    [InlineData("{\"property_id\":20011,\"index\":null}", "/index")]
    [InlineData("{\"property_id\":20011,\"index\":1.1}", "/index")]
    [InlineData("{\"property_id\":20011,\"index\":2147483648}", "/index")]
    [InlineData("{\"property_id\":20011,\"index\":\"1\"}", "/index")]
    [InlineData("{\"propertyId\":20011}", "/propertyId")]
    [InlineData("{\"property_id\":20011,\"property_name\":\"FUNC_TEXT\"}", "")]
    [InlineData("{\"property_id\":null,\"property_name\":\"FUNC_TEXT\"}", "")]
    public void Selector_InvalidShapeOrName_ReportsOriginalJsonPointer(string json, string suffix)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyAddressValidation.Selector(JToken.Parse(json), "Function", At, violations));
        Assert.Contains(violations, v => v.Path == At + suffix && v.Reason == ErrorReason.InvalidArgument);
    }

    [Theory]
    [InlineData("{\"property_id\":20011}", "Page")]
    [InlineData("{\"property_name\":\"FUNC_TEXT\"}", "Page")]
    [InlineData("{\"property_id\":20011}", "function")]
    [InlineData("{\"property_id\":20011}", "Unknown")]
    [InlineData("{\"property_name\":\"FUNC_TECHNICAL_CHARACTERISTIC\"}", "MDFunctionTemplatePosition")]
    public void Selector_AnotherObjectTypeOrNoPublicId_IsNotResolved(string json, string objectType)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyAddressValidation.Selector(JToken.Parse(json), objectType, At, violations));
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Single(violations).Reason);
    }

    [Theory]
    [InlineData("Page", "{\"property_id\":3010}")]
    [InlineData("Project", "{\"property_name\":\"PROPUSER_LAST_USEREMAIL\"}")]
    [InlineData("StorableObject", "{\"property_id\":2000}")]
    [InlineData("Function", "{\"property_id\":2000}")]
    [InlineData("Project", "{\"property_id\":10045}")]
    [InlineData("Page", "{\"property_name\":\"PAGE_CREATOR\"}")]
    public void Selector_ExcludedOrUnreviewedProperty_IsDeniedWithoutValueAccess(string objectType, string json)
    {
        var violations = new List<PropertyViolation>();
        Assert.Null(PropertyAddressValidation.Selector(JToken.Parse(json), objectType, At, violations));
        PropertyViolation violation = Assert.Single(violations);
        Assert.Equal(ErrorReason.PolicyDenied, violation.Reason);
        Assert.StartsWith(At + "/property_", violation.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Selector_ConflictingUnionWithInvalidFields_CollectsEveryViolation()
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Selector(JToken.Parse("""
            {"property_id":"20011","property_name":false,"index":1.5,"a~/b":true}
            """), "Function", At, violations);
        Assert.Equal(new[] { At, At + "/property_id", At + "/property_name", At + "/index", At + "/a~0~1b" }.Order(StringComparer.Ordinal),
            violations.Select(v => v.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("{\"project_object\":true}")]
    [InlineData("{\"page\":\"=AP+ST1/3\"}")]
    [InlineData("{\"device_tag\":\"=AP+ST1-K1\"}")]
    [InlineData("{\"device_tag\":\"=AP+ST1-K1\",\"page\":\"=AP+ST1/3\"}")]
    public void Target_FourAddressBranches_AreAccepted(string json)
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(JToken.Parse(json), Project, "/target", violations);
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("function")]
    [InlineData("connection")]
    public void Target_LocatorKindsAndProjectSpelling_ReuseExistingIdentityRules(string kind)
    {
        var target = new JObject
        {
            ["locator"] = new JObject
            {
                ["project"] = "x:\\fixture\\Line1.elk",
                ["kind"] = kind,
                ["type"] = 17,
                ["oid"] = long.MaxValue,
                ["name"] = "",
                ["page"] = null,
                ["category"] = null,
                ["pos"] = "100.5,-160",
            },
        };
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(target, Project, "/target", violations);
        Assert.Empty(violations);
    }

    public static TheoryData<string> ConflictingTargets()
    {
        var cases = new TheoryData<string>();
        string[] branches = ["\"project_object\":true", "\"page\":\"P\"", "\"device_tag\":\"K1\"", "\"locator\":{}"];
        for (int mask = 1; mask < 16; mask++)
        {
            if ((mask & (mask - 1)) == 0 || mask == 6)
            {
                continue;
            }

            cases.Add("{" + string.Join(",", branches.Where((_, i) => (mask & (1 << i)) != 0)) + "}");
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(ConflictingTargets))]
    public void Target_EveryConflictingUnion_IsRejected(string json)
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(JToken.Parse(json), Project, "/target", violations);
        Assert.Contains(violations, v => v.Path == "/target");
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("null", "")]
    [InlineData("[]", "")]
    [InlineData("\"page\"", "")]
    [InlineData("{\"project_object\":false}", "/project_object")]
    [InlineData("{\"project_object\":1}", "/project_object")]
    [InlineData("{\"project_object\":null}", "/project_object")]
    [InlineData("{\"page\":\" \"}", "/page")]
    [InlineData("{\"page\":false}", "/page")]
    [InlineData("{\"page\":null}", "/page")]
    [InlineData("{\"device_tag\":\"\"}", "/device_tag")]
    [InlineData("{\"device_tag\":[]}", "/device_tag")]
    [InlineData("{\"device_tag\":null}", "/device_tag")]
    [InlineData("{\"locator\":null}", "/locator")]
    [InlineData("{\"locator\":[]}", "/locator")]
    [InlineData("{\"owner\":{},\"article_reference_index\":1}", "/owner")]
    [InlineData("{\"project_object\":true,\"extra\":1}", "/extra")]
    public void Target_MissingUnsupportedOrInvalidBranch_IsRejected(string json, string suffix)
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(JToken.Parse(json), Project, "/target", violations);
        Assert.Contains(violations, v => v.Path == "/target" + suffix && v.Reason == ErrorReason.InvalidArgument);
    }

    [Theory]
    [InlineData("\"pos\":\"NaN,0\"", "pos")]
    [InlineData("\"pos\":\"0,Infinity\"", "pos")]
    [InlineData("\"pos\":\"1e999,0\"", "pos")]
    [InlineData("\"pos\":true", "pos")]
    [InlineData("\"oid\":1.0", "oid")]
    [InlineData("\"oid\":9223372036854775808", "oid")]
    [InlineData("\"type\":2147483648", "type")]
    [InlineData("\"kind\":\"article_reference\"", "kind")]
    [InlineData("\"kind\":null", "kind")]
    [InlineData("\"name\":5", "name")]
    public void Target_InvalidLocatorFields_AreCollectedWithoutCoercion(string field, string fieldName)
    {
        var locator = new JObject { ["project"] = Project, ["kind"] = "function", ["type"] = 17, ["oid"] = 42 };
        foreach (JProperty property in JObject.Parse("{" + field + "}").Properties())
        {
            locator[property.Name] = property.Value;
        }

        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(new JObject { ["locator"] = locator }, Project, "/target", violations);
        Assert.Equal("/target/locator/" + fieldName, Assert.Single(violations).Path);
    }

    [Fact]
    public void MultipleAddresses_AllIndependentErrors_AppendToTheSameViolationList()
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(JToken.Parse("""
            {"project_object":false,"page":" ","locator":{"project":"X:/fixture/Other","kind":"project","type":0,"oid":-1,"pos":"NaN,0","extra~/":true}}
            """), Project, "/queries/0/target", violations);
        PropertyAddressValidation.Selector(JToken.Parse("{\"property_id\":true,\"index\":false}"), "Function", At, violations);
        string root = "/queries/0/target";
        string[] expected = [root, root + "/project_object", root + "/page", root + "/locator/project", root + "/locator/kind",
            root + "/locator/type", root + "/locator/oid", root + "/locator/pos", root + "/locator/extra~0~1", At + "/property_id", At + "/index"];
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Path).Order(StringComparer.Ordinal));
        Assert.All(violations, v => Assert.Equal(ErrorReason.InvalidArgument, v.Reason));
        Assert.DoesNotContain(violations, v => v.Message.Contains("X:/", StringComparison.Ordinal));
    }

    [Fact]
    public void Target_EmptyLocator_ReportsEveryRequiredField()
    {
        var violations = new List<PropertyViolation>();
        PropertyAddressValidation.Target(JToken.Parse("{\"locator\":{}}"), Project, "/target", violations);
        string[] required = ["/target/locator/project", "/target/locator/kind", "/target/locator/type", "/target/locator/oid"];
        Assert.Equal(required, violations.Select(v => v.Path));
    }
}
