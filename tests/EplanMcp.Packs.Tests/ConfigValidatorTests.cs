using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class ConfigValidatorTests
{
    [Theory]
    [InlineData(ConfigFixtures.CompanyPath)]
    [InlineData(ConfigFixtures.CabinetTypePath)]
    public void Layer_MotorFixture_IsValidWithoutChangingItsBytes(string path)
    {
        KnowledgeDocument document = ConfigFixtures.Document(path);
        byte[] before = PackJson.Serialize(document);

        ConfigValidator.Layer(Element(document.Spec), _ => ConfigFixtures.Schema);

        Assert.Equal(before, PackJson.Serialize(document));
    }

    [Theory]
    [InlineData("unsupported-unit", "/spec/items/motor.power/unit")]
    [InlineData("choice-outside-enum", "/spec/settings/motor.starter/default")]
    [InlineData("unknown-layer", "/spec/layer")]
    [InlineData("missing-source", "/spec/schema")]
    [InlineData("unknown-key", "/spec/settings/motor.unknown")]
    public void Fixture_InvalidVariant_FailsAtItsPointer(string name, string expectedPath)
    {
        KnowledgeDocument document = KnowledgeDocument.Read(ConfigFixtures.Read($"InvalidConfig/{name}.json"));

        Problem(expectedPath, () =>
        {
            if (document.Kind == KnowledgeKind.ConfigSchema)
            {
                ConfigValidator.Schema(Element(document.Spec));
            }
            else
            {
                ConfigValidator.Layer(Element(document.Spec), source => source == "config_schema/motors" ? ConfigFixtures.Schema : null);
            }
        });
    }

    [Theory]
    [InlineData("W", "power", 1)]
    [InlineData("kW", "power", 1000)]
    [InlineData("V", "voltage", 1)]
    [InlineData("kV", "voltage", 1000)]
    [InlineData("A", "current", 1)]
    [InlineData("mA", "current", 0.001)]
    [InlineData("mm", "length", 0.001)]
    [InlineData("m", "length", 1)]
    public void Schema_RegisteredUnit_HasAConversionFactor(string unit, string dimension, double factor)
    {
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]!["unit"] = unit;

        ConfigValidator.Schema(Element(schema));

        Assert.Equal((dimension, (decimal)factor), ConfigValidator.Units[unit]);
    }

    [Theory]
    [InlineData("Motor.power", "Motor.power")]
    [InlineData("motor..power", "motor..power")]
    [InlineData("motor/power~value", "motor~1power~0value")]
    [InlineData("_power", "_power")]
    public void Schema_InvalidKey_UsesAnEscapedPointer(string key, string escaped)
    {
        JsonObject schema = SchemaNode();
        schema["items"]!.AsObject()[key] = schema["items"]!["motor.power"]!.DeepClone();

        Problem("/spec/items/" + escaped, () => ConfigValidator.Schema(Element(schema)));
    }

    [Fact]
    public void Schema_KeyWithSnakeCaseSegment_IsValid()
    {
        JsonObject schema = SchemaNode();
        schema["items"]!.AsObject()["motor.control_voltage"] = schema["items"]!["motor.voltage"]!.DeepClone();

        ConfigValidator.Schema(Element(schema));
    }

    [Theory]
    [InlineData("{}", "/spec/items")]
    [InlineData("{\"items\":[]}", "/spec/items")]
    [InlineData("{\"items\":{},\"extra/~\":true}", "/spec/extra~1~0")]
    public void Schema_InvalidSpecShape_FailsAtItsPointer(string json, string expectedPath) =>
        Problem(expectedPath, () => ConfigValidator.Schema(Parse(json)));

    [Theory]
    [InlineData("type", "\"object\"", "/type")]
    [InlineData("type", "null", "/type")]
    [InlineData("type", "3", "/type")]
    [InlineData("unit", "\"hp\"", "/unit")]
    [InlineData("unit", "\"kw\"", "/unit")]
    [InlineData("unit", "42", "/unit")]
    [InlineData("unit", "null", "/unit")]
    [InlineData("min", "\"0.06\"", "/min")]
    [InlineData("max", "true", "/max")]
    [InlineData("max", "0.01", "/max")]
    [InlineData("max", "1e1000", "/max")]
    [InlineData("required", "\"true\"", "/required")]
    [InlineData("required", "null", "/required")]
    [InlineData("allowedLayers", "[]", "/allowedLayers")]
    [InlineData("allowedLayers", "\"company\"", "/allowedLayers")]
    [InlineData("allowedLayers", "[\"company\",\"company\"]", "/allowedLayers/1")]
    [InlineData("allowedLayers", "[\"customer\"]", "/allowedLayers/0")]
    [InlineData("allowedLayers", "[null]", "/allowedLayers/0")]
    [InlineData("extra/~", "true", "/extra~1~0")]
    public void Schema_InvalidItemMember_FailsAtItsPointer(string member, string json, string suffix)
    {
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]![member] = JsonNode.Parse(json);

        Problem("/spec/items/motor.power" + suffix, () => ConfigValidator.Schema(Element(schema)));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("required")]
    [InlineData("allowedLayers")]
    public void Schema_MissingRequiredMember_IsRejected(string member)
    {
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]!.AsObject().Remove(member);

        Problem("/spec/items/motor.power/" + member, () => ConfigValidator.Schema(Element(schema)));
    }

    [Theory]
    [InlineData("text", "unit", "\"V\"")]
    [InlineData("boolean", "min", "0")]
    [InlineData("choice", "max", "10")]
    public void Schema_UnitOrBoundsOnWrongType_IsRejected(string type, string member, string value)
    {
        JsonObject schema = OneItem(type);
        schema["items"]!["key"]![member] = JsonNode.Parse(value);

        Problem("/spec/items/key/" + member, () => ConfigValidator.Schema(Element(schema)));
    }

    [Theory]
    [InlineData("choice", null, "/enum")]
    [InlineData("choice", "[]", "/enum")]
    [InlineData("choice", "null", "/enum")]
    [InlineData("choice", "[\"dol\",7]", "/enum/1")]
    [InlineData("choice", "[\"dol\",\"dol\"]", "/enum/1")]
    [InlineData("number", "[1,1.0]", "/enum/1")]
    [InlineData("boolean", "[\"true\"]", "/enum/0")]
    [InlineData("text", "[{}]", "/enum/0")]
    public void Schema_InvalidEnum_IsRejected(string type, string? choices, string suffix)
    {
        JsonObject schema = OneItem(type);
        if (choices is not null)
        {
            schema["items"]!["key"]!["enum"] = JsonNode.Parse(choices);
        }

        Problem("/spec/items/key" + suffix, () => ConfigValidator.Schema(Element(schema)));
    }

    [Fact]
    public void Schema_EnumValueOutsideBounds_IsRejected()
    {
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]!["enum"] = JsonNode.Parse("[7.5,251]");

        Problem("/spec/items/motor.power/enum/1", () => ConfigValidator.Schema(Element(schema)));
    }

    [Theory]
    [InlineData("text", "\"panel\"")]
    [InlineData("text", "\"\"")]
    [InlineData("number", "7.5")]
    [InlineData("boolean", "true")]
    [InlineData("boolean", "false")]
    [InlineData("choice", "\"dol\"")]
    public void Settings_TypedDefaultsAndConstraints_AreValid(string type, string value)
    {
        JsonObject schema = OneItem(type);
        if (type == "choice")
        {
            schema["items"]!["key"]!["enum"] = JsonNode.Parse("[\"dol\",\"star_delta\"]");
        }

        ConfigValidator.ValidateSettings(Element(schema), Parse($$$$"""{"key":{"default":{{{{value}}}},"constraint":{"fixed":{{{{value}}}}}}}"""), "company");
        ConfigValidator.ValidateSettings(Element(schema), Parse($$$$"""{"key":{"constraint":{"allowed":[{{{{value}}}}]}}}"""), "company");
    }

    [Theory]
    [InlineData("text", "7")]
    [InlineData("number", "\"7.5\"")]
    [InlineData("boolean", "\"true\"")]
    [InlineData("boolean", "1")]
    [InlineData("choice", "true")]
    public void Settings_WrongScalarType_IsRejectedWithoutCoercion(string type, string value)
    {
        JsonObject schema = OneItem(type);
        if (type == "choice")
        {
            schema["items"]!["key"]!["enum"] = JsonNode.Parse("[\"dol\"]");
        }

        Problem("/spec/settings/key/default", () => ConfigValidator.ValidateSettings(Element(schema), Parse($$$$"""{"key":{"default":{{{{value}}}}}}"""), "company"));
    }

    [Theory]
    [InlineData("{\"default\":0.05}", "/default")]
    [InlineData("{\"default\":251}", "/default")]
    [InlineData("{\"default\":1e1000}", "/default")]
    [InlineData("{\"default\":{\"value\":7.5}}", "/default")]
    [InlineData("{\"default\":null}", "/default")]
    [InlineData("{\"constraint\":{\"fixed\":\"7.5\"}}", "/constraint/fixed")]
    [InlineData("{\"constraint\":{\"allowed\":[7.5,251]}}", "/constraint/allowed/1")]
    [InlineData("{\"constraint\":{\"allowed\":[7.5,true]}}", "/constraint/allowed/1")]
    [InlineData("{\"constraint\":{\"allowed\":[7.5,7.50]}}", "/constraint/allowed/1")]
    [InlineData("{\"constraint\":{\"allowed\":{\"min\":0,\"max\":100}}}", "/constraint/allowed/min")]
    [InlineData("{\"constraint\":{\"allowed\":{\"min\":1,\"max\":251}}}", "/constraint/allowed/max")]
    [InlineData("{\"constraint\":{\"allowed\":{\"min\":10,\"max\":5}}}", "/constraint/allowed/max")]
    [InlineData("{\"constraint\":{\"allowed\":[]}}", "/constraint/allowed")]
    [InlineData("{\"constraint\":{\"allowed\":7.5}}", "/constraint/allowed")]
    [InlineData("{\"constraint\":{}}", "/constraint")]
    [InlineData("{\"constraint\":{\"fixed\":7.5,\"allowed\":[7.5]}}", "/constraint")]
    [InlineData("{\"constraint\":{\"allowed\":{\"min\":1}}}", "/constraint/allowed/max")]
    [InlineData("{\"constraint\":{\"allowed\":{\"min\":1,\"max\":10,\"open\":true}}}", "/constraint/allowed/open")]
    [InlineData("{\"required\":true}", "/required")]
    [InlineData("{\"unit\":\"W\"}", "/unit")]
    [InlineData("{\"type\":\"text\"}", "/type")]
    public void Settings_InvalidDefaultOrConstraint_FailsAtItsPointer(string setting, string suffix) =>
        Problem("/spec/settings/motor.power" + suffix, () => ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Parse($$$$"""{"motor.power":{{{{setting}}}}}"""), "company"));

    [Fact]
    public void Settings_ClosedNumberInterval_IncludesBothSchemaBounds() =>
        ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Parse("""{"motor.power":{"default":0.06,"constraint":{"allowed":{"min":0.06,"max":250}}}}"""), "company");

    [Theory]
    [InlineData("text")]
    [InlineData("boolean")]
    [InlineData("choice")]
    public void Settings_IntervalOnNonNumber_IsRejected(string type)
    {
        JsonObject schema = OneItem(type);
        if (type == "choice")
        {
            schema["items"]!["key"]!["enum"] = JsonNode.Parse("[\"dol\"]");
        }

        Problem("/spec/settings/key/constraint/allowed", () => ConfigValidator.ValidateSettings(Element(schema), Parse("""{"key":{"constraint":{"allowed":{"min":1,"max":2}}}}"""), "company"));
    }

    [Theory]
    [InlineData("{\"constraint\":{\"fixed\":\"soft_starter\"}}", "/fixed")]
    [InlineData("{\"constraint\":{\"allowed\":[\"dol\",\"soft_starter\"]}}", "/allowed/1")]
    public void Settings_ChoiceConstraintOutsideEnum_IsRejected(string setting, string suffix) =>
        Problem("/spec/settings/motor.starter/constraint" + suffix, () => ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Parse($$$$"""{"motor.starter":{{{{setting}}}}}"""), "company"));

    [Theory]
    [InlineData("base")]
    [InlineData("company")]
    [InlineData("cabinet_type")]
    [InlineData("project")]
    [InlineData("cabinet")]
    public void Settings_LayerPermission_IsEnforced(string layer)
    {
        JsonElement settings = Parse("""{"motor.power":{"default":7.5}}""");
        ConfigValidator.ValidateSettings(ConfigFixtures.Schema, settings, layer);
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]!["allowedLayers"] = new JsonArray(layer == "company" ? "cabinet" : "company");

        Problem("/spec/settings/motor.power", () => ConfigValidator.ValidateSettings(Element(schema), settings, layer));
    }

    [Theory]
    [InlineData("schema", "\"module/motors\"", "/spec/schema")]
    [InlineData("schema", "\"sample.motors@1.0.0:config_schema/motors\"", "/spec/schema")]
    [InlineData("schema", "\"config_schema/../motors\"", "/spec/schema")]
    [InlineData("schema", "7", "/spec/schema")]
    [InlineData("layer", "true", "/spec/layer")]
    [InlineData("layer", "\"Company\"", "/spec/layer")]
    [InlineData("layer", "\"base\"", "/spec/layer")]
    [InlineData("layer", "\"project\"", "/spec/layer")]
    [InlineData("layer", "\"cabinet\"", "/spec/layer")]
    [InlineData("required", "true", "/spec/required")]
    [InlineData("settings", "[]", "/spec/settings")]
    public void Layer_InvalidMember_IsRejected(string member, string value, string expectedPath)
    {
        JsonObject spec = ConfigFixtures.Document(ConfigFixtures.CompanyPath).Spec.DeepClone().AsObject();
        spec[member] = JsonNode.Parse(value);

        Problem(expectedPath, () => ConfigValidator.Layer(Element(spec), _ => ConfigFixtures.Schema));
    }

    [Theory]
    [InlineData("{\"unknown/~\":{\"default\":true}}", "/spec/settings/unknown~1~0")]
    [InlineData("{\"motor.unknown\":{\"default\":true}}", "/spec/settings/motor.unknown")]
    public void Settings_UnknownKey_IsRejectedWithEscapedPointer(string settings, string expectedPath) =>
        Problem(expectedPath, () => ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Parse(settings), "company"));

    [Fact]
    public void Layer_RequiredValuesAreAbsent_IsValidUntilMerge() =>
        ConfigValidator.Layer(Parse("""{"layer":"company","schema":"config_schema/motors","settings":{}}"""), _ => ConfigFixtures.Schema);

    [Theory]
    [InlineData("7.5", true)]
    [InlineData("\"7.5\"", false)]
    public void Schema_Default_IsValidatedAtBase(string value, bool valid)
    {
        JsonObject schema = SchemaNode();
        schema["items"]!["motor.power"]!["default"] = JsonNode.Parse(value);
        if (valid)
        {
            ConfigValidator.Schema(Element(schema));
            schema["items"]!["motor.power"]!["allowedLayers"] = new JsonArray("company");
        }

        Problem("/spec/items/motor.power/default", () => ConfigValidator.Schema(Element(schema)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Binding_ProjectAndCabinetValues_UseTheSameSchemaValidator(bool project)
    {
        ProjectBinding binding = PackJson.ReadBinding(Encoding.UTF8.GetBytes(VersionedReadModelTests.Binding));
        JsonObject values = project ? binding.ProjectValues! : binding.Cabinets[0].Values!;
        string expectedPath = project ? "/projectValues" : "/cabinets/0/values";
        string layer = project ? "project" : "cabinet";
        values["motor.power"] = JsonNode.Parse("""{"default":7.5,"constraint":{"allowed":{"min":0.5,"max":10}}}""");
        ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Element(values), layer, expectedPath);
        values["motor.power"]!["default"] = "7.5";

        Problem(expectedPath + "/motor.power/default", () => ConfigValidator.ValidateSettings(ConfigFixtures.Schema, Element(values), layer, expectedPath));
    }

    private static JsonObject SchemaNode() => ConfigFixtures.Document(ConfigFixtures.SchemaPath).Spec.DeepClone().AsObject();

    private static JsonObject OneItem(string type) => JsonNode.Parse($$$$"""{"items":{"key":{"type":"{{{{type}}}}","allowedLayers":["company"],"required":false}}}""")!.AsObject();

    private static JsonElement Parse(string json) => Element(JsonNode.Parse(json)!);

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node, PackJson.Options);

    private static void Problem(string expectedPath, Action action) => Assert.StartsWith(expectedPath + ":", Assert.Throws<PackFormatException>(action).Message, StringComparison.Ordinal);
}
