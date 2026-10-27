using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class ModuleValidatorTests
{
    [Fact]
    public void Module_MotorFixture_PreservesBindingsPortsAndEvidence()
    {
        ModuleDefinition module = ModuleFixtures.Module(ModuleFixtures.Body("motor-starter"));

        Assert.Equal("number", module.ParameterSchema.GetProperty("items").GetProperty("power").GetProperty("type").GetString());
        Assert.Equal(20011, module.Spec.GetProperty("parameters").GetProperty("power").GetProperty("binding").GetProperty("propertyId").GetInt32());
        Assert.Equal(2, module.Spec.GetProperty("interface").GetProperty("ports").GetArrayLength());
        Assert.Equal("all", module.Applicability.Operator);
        Assert.Equal(2, module.Spec.GetProperty("extraction").GetProperty("observations").GetArrayLength());
        Assert.Equal(4, module.Spec.GetProperty("acceptance").GetProperty("checks").GetArrayLength());
    }

    [Theory]
    [InlineData("/parameters/power/type", "\"text\"", "/parameters/power/unit")]
    [InlineData("/parameters/power/unit", "\"rpm\"", "/parameters/power/unit")]
    [InlineData("/parameters/power/default", "300", "/parameters/power/default")]
    [InlineData("/parameters/starter/enum", "[]", "/parameters/starter/enum")]
    [InlineData("/parameters/power/required", "\"true\"", "/parameters/power/required")]
    [InlineData("/parameters/power/binding/propertyId", "0", "/parameters/power/binding")]
    [InlineData("/parameters/power/binding/language", "3", "/parameters/power/binding/language")]
    [InlineData("/parameters/power/binding/extra", "true", "/parameters/power/binding/extra")]
    [InlineData("/valueSets/0/values/power", "\"7.5\"", "/valueSets/0/values/power")]
    [InlineData("/valueSets/0/values/power", "300", "/valueSets/0/values/power")]
    [InlineData("/valueSets/0/values/starter", "\"soft\"", "/valueSets/0/values/starter")]
    [InlineData("/valueSets/0/values/extra", "false", "/valueSets/0/values/extra")]
    [InlineData("/valueSets/1/name", "\"dol-7.5\"", "/valueSets/1/name")]
    [InlineData("/valueSets/0/values/power", "1e1000", "/valueSets/0/values/power")]
    [InlineData("/interface/ports/0/object", "\"C:/outside\"", "/interface/ports/0/object")]
    [InlineData("/interface/ports/0/direction", "\"any\"", "/interface/ports/0/direction")]
    [InlineData("/interface/ports/0/kind", "\"inferred_net\"", "/interface/ports/0/kind")]
    [InlineData("/interface/ports/0/connectionPoint", "null", "/interface/ports/0/connectionPoint")]
    [InlineData("/interface/ports/1/name", "\"supply\"", "/interface/ports/1/name")]
    [InlineData("/interface/ports/0/extra", "true", "/interface/ports/0/extra")]
    [InlineData("/macro/hash", "\"changed\"", "/macro/hash")]
    [InlineData("/macro/asset", "\"document/guide.md\"", "/macro/asset")]
    [InlineData("/macro/variant", "1", "/macro")]
    [InlineData("/macro/numbering", "\"renumber\"", "/macro")]
    [InlineData("/partMapping/asset", "\"material_mapping/missing\"", "/partMapping/asset")]
    [InlineData("/partMapping/configKey", "\"motor.mapping\"", "/partMapping")]
    [InlineData("/applicability", "{\"exists\":[{\"parameter\":\"unknown\"}]}", "/applicability/exists/0/parameter")]
    [InlineData("/applicability", "{\"literal\":7.5}", "/applicability")]
    [InlineData("/applicability", "{\"exists\":[{\"property\":\"function.name\"}]}", "/applicability")]
    [InlineData("/acceptance/checks/0/source/hash", "\"changed\"", "/acceptance/checks/0/source/hash")]
    [InlineData("/acceptance/checks/0/source/asset", "\"document/missing\"", "/acceptance/checks/0/source/asset")]
    [InlineData("/acceptance/checks/0/coverage", "[]", "/acceptance/checks/0/coverage")]
    [InlineData("/acceptance/checks/2/port", "\"missing\"", "/acceptance/checks/2/port")]
    [InlineData("/acceptance/checks/1/expected", "{}", "/acceptance/checks/1/expected")]
    [InlineData("/acceptance/checks/0/port", "\"supply\"", "/acceptance/checks/0/port")]
    [InlineData("/acceptance/samples/0", "\"sample/missing\"", "/acceptance/samples/0")]
    [InlineData("/extraction/snapshotHash", "\"changed\"", "/extraction")]
    [InlineData("/extraction/locators/0", "\"C:/source/project.elk\"", "/extraction/locators/0")]
    [InlineData("/extraction/DatabaseIdentifier", "\"123\"", "/extraction/DatabaseIdentifier")]
    [InlineData("/extra", "true", "/extra")]
    public void Module_InvalidMetadata_ReportsPointer(string path, string json, string expectedPointer)
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        ModuleFixtures.Set(body, path, json);

        PackFormatException error = Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body));

        Assert.StartsWith("/spec" + expectedPointer + ":", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_UnknownMemberWithPointerCharacters_EscapesItsLocation()
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        body["unknown~/field"] = true;

        Assert.StartsWith("/spec/unknown~0~1field:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_TextBooleanAndLanguageBinding_AreTypedWithoutCoercion()
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        body["parameters"]!["label"] = JsonNode.Parse("""{"type":"text","required":true,"default":"Motor","binding":{"placeholder":"starter","variable":"label","propertyId":20013,"language":"en_US"}}""");
        body["parameters"]!["enabled"] = JsonNode.Parse("""{"type":"boolean","required":true,"default":true,"binding":{"placeholder":"starter","variable":"enabled","propertyId":20014}}""");
        Assert.NotNull(ModuleFixtures.Module(body));
        body["valueSets"]![0]!["values"]!["enabled"] = "true";

        Assert.StartsWith("/spec/valueSets/0/values/enabled:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_MissingRequiredValueOrBinding_IsRefused()
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        body["valueSets"]![0]!["values"]!.AsObject().Remove("power");
        Assert.StartsWith("/spec/valueSets/0/values/power:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
        body["parameters"]!["power"]!["default"] = 7.5m;
        Assert.NotNull(ModuleFixtures.Module(body));
        body["parameters"]!["power"]!.AsObject().Remove("binding");
        Assert.StartsWith("/spec/parameters/power/binding:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_DuplicateVariableBinding_IsRefused()
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        body["parameters"]!["starter"]!["binding"] = body["parameters"]!["power"]!["binding"]!.DeepClone();

        Assert.StartsWith("/spec/parameters/starter/binding:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_ConfigSelectedMapping_RequiresDeclaredTextKey()
    {
        JsonObject body = ModuleFixtures.Body("motor-starter");
        body["partMapping"] = JsonNode.Parse("""{"configKey":"motor.mapping"}""");
        Assert.NotNull(ModuleFixtures.Module(body));
        body["partMapping"]!["configKey"] = "motor.power";

        Assert.StartsWith("/spec/partMapping/configKey:", Assert.Throws<PackFormatException>(() => ModuleFixtures.Module(body)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_TwoCompanies_SelectExactPartsAndVariants()
    {
        MaterialMappingDefinition first = ModuleValidator.Mapping(ModuleFixtures.Spec("company-a"), ModuleFixtures.Resolve);
        MaterialMappingDefinition second = ModuleValidator.Mapping(ModuleFixtures.Spec("company-b"), ModuleFixtures.Resolve);
        JsonElement dol = ModuleFixtures.Element(JsonNode.Parse("""{"power":7.5,"starter":"dol"}""")!);
        JsonElement starDelta = ModuleFixtures.Element(JsonNode.Parse("""{"power":7.5,"starter":"star_delta"}""")!);

        Assert.Equal("SIE.3RT2015-1BB41", first.Select(dol).GetProperty("part").GetString());
        Assert.Equal("O2.TEST-DOL-075", second.Select(dol).GetProperty("part").GetString());
        Assert.Equal(1, second.Select(starDelta).GetProperty("variant").GetInt32());
        Assert.Equal(3, second.Select(starDelta).GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData("/entries/1/when", "{\"power\":7.5}", "/entries/1/when")]
    [InlineData("/entries/1/when", "{\"starter\":\"dol\",\"power\":7.50}", "/entries/1/when")]
    [InlineData("/entries/0/when/power", "\"7.5\"", "/entries/0/when/power")]
    [InlineData("/entries/0/when/unknown", "1", "/entries/0/when/unknown")]
    [InlineData("/entries/0/when", "{}", "/entries/0/when")]
    [InlineData("/entries/0/variant", "0.5", "/entries/0/variant")]
    [InlineData("/entries/0/quantity", "0", "/entries/0/quantity")]
    [InlineData("/entries/0/part", "[]", "/entries/0/part")]
    [InlineData("/entries/0/source", "\"confirmed\"", "/entries/0/source")]
    [InlineData("/entries/0/source/extra", "true", "/entries/0/source/extra")]
    public void Mapping_InvalidOrAmbiguousEntry_ReportsPointer(string path, string json, string expectedPointer)
    {
        JsonObject body = ModuleFixtures.Body("company-a");
        ModuleFixtures.Set(body, path, json);

        PackFormatException error = Assert.Throws<PackFormatException>(() => ModuleValidator.Mapping(ModuleFixtures.Element(body), ModuleFixtures.Resolve));

        Assert.StartsWith("/spec" + expectedPointer + ":", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_MissingMatchAndInvalidInput_DoNotFallback()
    {
        MaterialMappingDefinition mapping = ModuleValidator.Mapping(ModuleFixtures.Spec("company-a"), ModuleFixtures.Resolve);

        Assert.Contains("RESOURCE_NOT_FOUND", Assert.Throws<PackFormatException>(() => mapping.Select(ModuleFixtures.Element(JsonNode.Parse("""{"power":5.5,"starter":"dol"}""")!))).Message, StringComparison.Ordinal);
        Assert.StartsWith("/values/power:", Assert.Throws<PackFormatException>(() => mapping.Select(ModuleFixtures.Element(JsonNode.Parse("""{"power":"7.5","starter":"dol"}""")!))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_UnvalidatedAmbiguousModel_SelectStillRejectsMultipleMatches()
    {
        MaterialMappingDefinition valid = ModuleValidator.Mapping(ModuleFixtures.Spec("company-a"), ModuleFixtures.Resolve);
        JsonObject body = ModuleFixtures.Body("company-a");
        body["entries"]!.AsArray().Add(body["entries"]![0]!.DeepClone());
        var invalid = new MaterialMappingDefinition(ModuleFixtures.Element(body), valid.ParameterSchema);

        Assert.Contains("CONFIG_CONFLICT", Assert.Throws<PackFormatException>(() => invalid.Select(ModuleFixtures.Element(JsonNode.Parse("""{"power":7.5,"starter":"dol"}""")!))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_DefaultAndAbsentOptionalParameter_AreHandledWithoutCoercion()
    {
        JsonObject body = ModuleFixtures.Body("company-a");
        body["parameters"]!["power"]!["default"] = 7.5m;
        MaterialMappingDefinition mapping = ModuleValidator.Mapping(ModuleFixtures.Element(body), ModuleFixtures.Resolve);
        Assert.Equal("SIE.3RT2015-1BB41", mapping.Select(ModuleFixtures.Element(JsonNode.Parse("""{"starter":"dol"}""")!)).GetProperty("part").GetString());
        body["parameters"]!["power"]!.AsObject().Remove("default");
        body["parameters"]!["power"]!["required"] = false;
        mapping = ModuleValidator.Mapping(ModuleFixtures.Element(body), ModuleFixtures.Resolve);
        Assert.Contains("RESOURCE_NOT_FOUND", Assert.Throws<PackFormatException>(() => mapping.Select(ModuleFixtures.Element(JsonNode.Parse("""{"starter":"dol"}""")!))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sample_ValidReferencesAndSources_PreservesBody()
    {
        JsonElement original = ModuleFixtures.Spec("motor-acceptance");
        SampleDefinition sample = ModuleValidator.Sample(original, ModuleFixtures.Resolve);

        Assert.True(JsonElement.DeepEquals(original, sample.Spec));
    }

    [Theory]
    [InlineData("/sources", "[]", "/sources")]
    [InlineData("/sources/0/hash", "\"changed\"", "/sources/0/hash")]
    [InlineData("/sources/0/location", "\"C:/customer\"", "/sources/0/location")]
    [InlineData("/sources/0/asset", "\"workflow/motor\"", "/sources/0/asset")]
    [InlineData("/workflow", "\"module/motor-starter\"", "/workflow")]
    [InlineData("/configFixture", "\"config_layer/missing\"", "/configFixture")]
    [InlineData("/expectedChecks/0", "\"sample/motor-acceptance\"", "/expectedChecks/0")]
    [InlineData("/expectedObjects/0", "\"C:/outside\"", "/expectedObjects/0")]
    [InlineData("/inputs", "[]", "/inputs")]
    [InlineData("/extra", "true", "/extra")]
    public void Sample_InvalidAcceptanceSourceOrReference_ReportsPointer(string path, string json, string expectedPointer)
    {
        JsonObject body = ModuleFixtures.Body("motor-acceptance");
        ModuleFixtures.Set(body, path, json);

        PackFormatException error = Assert.Throws<PackFormatException>(() => ModuleValidator.Sample(ModuleFixtures.Element(body), ModuleFixtures.Resolve));

        Assert.StartsWith("/spec" + expectedPointer + ":", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("motor-starter")]
    [InlineData("company-a")]
    public void LegacyDocument_ReadAndSerialize_DoesNotUpgrade(string name)
    {
        KnowledgeDocument original = ModuleFixtures.Document(name) with { ApiVersion = KnowledgeDocument.CurrentApiVersion };
        byte[] bytes = PackJson.Serialize(original);

        KnowledgeDocument read = KnowledgeDocument.Read(bytes);

        Assert.Equal(KnowledgeDocument.CurrentApiVersion, read.ApiVersion);
        Assert.Equal(bytes, PackJson.Serialize(read));
    }
}
