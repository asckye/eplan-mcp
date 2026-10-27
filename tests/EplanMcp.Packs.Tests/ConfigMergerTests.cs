using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class ConfigMergerTests
{
    private static readonly string[] _constraintLayers = ["base", "company", "cabinet_type"];
    private static readonly string[] _exampleLayers = ["base", "company", "cabinet_type", "cabinet"];
    private static readonly string[] _exampleKinds = ["default", "constraint", "default", "default"];
    private static readonly string[] _allLayers = ["base", "company", "cabinet_type", "project", "cabinet"];
    private static readonly string[] _sortedKeys = ["a", "z"];

    [Theory]
    [InlineData("{\"allowed\":[1,5,10]}", "{\"allowed\":[1,5]}", true)]
    [InlineData("{\"allowed\":[1,5]}", "{\"allowed\":[1,5,10]}", false)]
    [InlineData("{\"allowed\":[1,5]}", "{\"allowed\":[5,10]}", false)]
    [InlineData("{\"allowed\":[1,5]}", "{\"allowed\":[10]}", false)]
    [InlineData("{\"allowed\":[1,5]}", "{\"fixed\":5}", true)]
    [InlineData("{\"allowed\":[1,5]}", "{\"fixed\":10}", false)]
    [InlineData("{\"allowed\":[1,5]}", "{\"allowed\":{\"min\":5,\"max\":5}}", true)]
    [InlineData("{\"allowed\":[1,5]}", "{\"allowed\":{\"min\":1,\"max\":5}}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":{\"min\":2,\"max\":8}}", true)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":{\"min\":0,\"max\":10}}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":{\"min\":1,\"max\":11}}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":{\"min\":10,\"max\":11}}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":{\"min\":11,\"max\":12}}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":[1,5,10]}", true)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"allowed\":[0,5]}", false)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"fixed\":5}", true)]
    [InlineData("{\"allowed\":{\"min\":1,\"max\":10}}", "{\"fixed\":11}", false)]
    [InlineData("{\"fixed\":5}", "{\"fixed\":5.0}", true)]
    [InlineData("{\"fixed\":5}", "{\"fixed\":10}", false)]
    [InlineData("{\"fixed\":5}", "{\"allowed\":[5]}", true)]
    [InlineData("{\"fixed\":5}", "{\"allowed\":[1,5]}", false)]
    [InlineData("{\"fixed\":5}", "{\"allowed\":{\"min\":5,\"max\":5}}", true)]
    [InlineData("{\"fixed\":5}", "{\"allowed\":{\"min\":1,\"max\":5}}", false)]
    public void Merge_ConstraintCombinations_OnlySubsetsSucceed(string upper, string lower, bool valid)
    {
        ConfigDocument[] layers = [Layer(Setting(upper)), Layer(Setting(lower), "cabinet_type")];
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], layers, Binding(), 0);

        Assert.Equal(valid, result.Ok);
        if (valid)
        {
            JsonElement value = Assert.Single(result.Values.EnumerateArray());
            Assert.Equal(5m, value.GetProperty("value").GetDecimal());
            Assert.True(JsonElement.DeepEquals(Parse(lower), value.GetProperty("constraints")));
            Assert.Equal(3, value.GetProperty("trace").GetArrayLength());
        }
        else
        {
            Conflict(result, "/spec/settings/key/constraint");
            Assert.Equal(_constraintLayers, result.Trace.EnumerateArray().Select(t => t.GetProperty("layer").GetString()));
        }
    }

    [Fact]
    public void Merge_DesignExample_ReturnsDolAndFourVisibleSources()
    {
        ConfigDocument schema = Schema("""{"type":"text","default":"dol"}""");
        ConfigDocument[] layers =
        [
            Layer("""{"key":{"constraint":{"allowed":["dol","star_delta"]}}}"""),
            Layer("""{"key":{"default":"star_delta"}}""", "cabinet_type"),
        ];

        ConfigMergeResult result = ConfigMerger.Merge([schema], layers, Binding(cabinet: """{"key":{"default":"dol"}}"""), 0);

        Assert.True(result.Ok, result.Message);
        JsonElement value = Assert.Single(result.Values.EnumerateArray());
        Assert.Equal("dol", value.GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("unit").ValueKind);
        JsonElement[] trace = [.. value.GetProperty("trace").EnumerateArray()];
        Assert.Equal(_exampleLayers, trace.Select(t => t.GetProperty("layer").GetString()));
        Assert.Equal(_exampleKinds, trace.Select(t => t.GetProperty("kind").GetString()));
        Assert.Equal(schema.Resource, trace[0].GetProperty("source").GetProperty("resource").GetString());
        Assert.Equal(schema.Sha256, trace[0].GetProperty("source").GetProperty("sha256").GetString());
        Assert.Equal("/spec/items/key/default", trace[0].GetProperty("pointer").GetString());
        Assert.Equal("/cabinets/0/values/key/default", trace[3].GetProperty("pointer").GetString());
        Assert.Equal(7, trace[3].GetProperty("source").GetProperty("bindingRevision").GetInt32());
    }

    [Fact]
    public void Merge_FiveIdenticalDefaults_KeepsEveryLayerInOrderWithoutMutatingInputs()
    {
        ConfigDocument schema = Schema();
        ConfigDocument[] layers = [Layer("""{"key":{"default":5}}"""), Layer("""{"key":{"default":5.0}}""", "cabinet_type")];
        ProjectBinding binding = Binding("""{"key":{"default":5}}""", """{"key":{"default":5}}""");
        byte[] before = PackJson.Serialize(binding);

        ConfigMergeResult result = ConfigMerger.Merge([schema], layers, binding, 0);

        Assert.True(result.Ok, result.Message);
        JsonElement value = Assert.Single(result.Values.EnumerateArray());
        Assert.Equal(5, value.GetProperty("trace").GetArrayLength());
        Assert.Equal(_allLayers, value.GetProperty("trace").EnumerateArray().Select(t => t.GetProperty("layer").GetString()));
        Assert.Equal(before, PackJson.Serialize(binding));
        Assert.Equal(5m, schema.Spec.GetProperty("items").GetProperty("key").GetProperty("default").GetDecimal());
    }

    [Theory]
    [InlineData("{\"fixed\":10}")]
    [InlineData("{\"allowed\":[1,10]}")]
    [InlineData("{\"allowed\":{\"min\":6,\"max\":10}}")]
    public void Merge_FinalDefaultOutsideConstraint_IsAConflict(string constraint) =>
        Assert.Equal(ConfigMerger.ConfigConflict, ConfigMerger.Merge([Schema()], [Layer(Setting(constraint))], Binding(), 0).Reason);

    [Fact]
    public void Merge_IntermediateDefaultOutsideConstraint_CanBeReplacedBeforeFinalValidation()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [Layer(Setting("""{"fixed":10}"""))], Binding(cabinet: """{"key":{"default":10}}"""), 0);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(10m, result.Values[0].GetProperty("value").GetDecimal());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"key\":{\"constraint\":{\"allowed\":[5]}}}")]
    [InlineData("{\"key\":{\"constraint\":{\"fixed\":5}}}")]
    public void Merge_RequiredWithoutDefault_DoesNotPickAConstraintValue(string settings) =>
        Conflict(ConfigMerger.Merge([Schema("""{"type":"number"}""")], [Layer(settings)], Binding(), 0), "/spec/items/key/required");

    [Fact]
    public void Merge_OptionalWithoutDefault_ReturnsNullWithConstraintsAndTrace()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema("""{"type":"number","required":false}""")], [Layer(Setting("""{"allowed":[5]}"""))], Binding(), 0);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(JsonValueKind.Null, result.Values[0].GetProperty("value").ValueKind);
        Assert.Equal(2, result.Values[0].GetProperty("trace").GetArrayLength());
    }

    [Theory]
    [InlineData("{\"type\":\"number\",\"default\":5,\"min\":1}", "{\"allowed\":{\"min\":1,\"max\":10}}")]
    [InlineData("{\"type\":\"number\",\"default\":5,\"max\":10}", "{\"allowed\":{\"min\":1,\"max\":10}}")]
    [InlineData("{\"type\":\"number\",\"default\":5,\"enum\":[1,5,10]}", "{\"allowed\":[1,5]}")]
    [InlineData("{\"type\":\"boolean\",\"default\":false}", "{\"fixed\":false}")]
    public void Merge_SchemaBoundsAndEnums_ArePreservedAndNarrowed(string item, string constraint)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema(item)], [Layer(Setting(constraint))], Binding(), 0);

        Assert.True(result.Ok, result.Message);
        Assert.True(JsonElement.DeepEquals(Parse(constraint), result.Values[0].GetProperty("constraints")));
    }

    [Fact]
    public void Merge_EquivalentKilowattAndWattSchemas_ConvertsBeforeComparingAndRetainsRawTrace()
    {
        ConfigDocument kilowatts = Schema("""{"type":"number","unit":"kW","default":7.5,"min":0.06,"max":250}""");
        ConfigDocument watts = Schema("""{"type":"number","unit":"W","default":7500,"min":60,"max":250000}""", "other.motors");
        ConfigDocument[] layers =
        [
            Layer(Setting("""{"allowed":{"min":1,"max":10}}""")),
            Layer("""{"key":{"default":8000,"constraint":{"allowed":{"min":7500,"max":9000}}}}""", "cabinet_type", "other.motors"),
        ];

        ConfigMergeResult result = ConfigMerger.Merge([kilowatts, watts], layers, Binding(), 0);

        Assert.True(result.Ok, result.Message);
        JsonElement value = result.Values[0];
        Assert.Equal("kW", value.GetProperty("unit").GetString());
        Assert.Equal(8m, value.GetProperty("value").GetDecimal());
        Assert.True(JsonElement.DeepEquals(Parse("""{"allowed":{"min":7.5,"max":9}}"""), value.GetProperty("constraints")));
        Assert.Equal(8000m, value.GetProperty("trace").EnumerateArray().Last().GetProperty("value").GetDecimal());
    }

    [Theory]
    [InlineData("{\"allowed\":[7500,11000]}")]
    [InlineData("{\"allowed\":{\"min\":7500,\"max\":11000}}")]
    [InlineData("{\"fixed\":11000}")]
    public void Merge_WattsWidenEarlierKilowattConstraint_IsAConflict(string constraint)
    {
        ConfigDocument[] schemas = [Schema("""{"type":"number","unit":"kW","default":7.5}"""), Schema("""{"type":"number","unit":"W","default":7500}""", "other.motors")];
        ConfigDocument[] layers = [Layer(Setting("""{"allowed":{"min":1,"max":10}}""")), Layer(Setting(constraint), "cabinet_type", "other.motors")];

        Conflict(ConfigMerger.Merge(schemas, layers, Binding(), 0), "/spec/settings/key/constraint");
    }

    [Theory]
    [InlineData(true, "{\"unknown/~\":{\"default\":5}}", "/unknown~1~0")]
    [InlineData(false, "{\"unknown/~\":{\"default\":5}}", "/unknown~1~0")]
    [InlineData(true, "{\"key\":{\"default\":\"5\"}}", "/key/default")]
    [InlineData(false, "{\"key\":{\"default\":\"5\"}}", "/key/default")]
    [InlineData(true, "{\"key\":{\"unit\":\"W\"}}", "/key/unit")]
    [InlineData(false, "{\"key\":{\"unit\":\"W\"}}", "/key/unit")]
    public void Merge_BindingSettingsAreValidatedAgainstMergedSchema_WithTheirOwnPointers(bool project, string settings, string suffix)
    {
        ProjectBinding binding = project ? Binding(project: settings) : Binding(cabinet: settings);

        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [], binding, 0);

        Assert.Equal(ConfigMerger.InvalidArgument, result.Reason);
        Assert.Equal((project ? "/projectValues" : "/cabinets/0/values") + suffix, result.Location);
        Assert.Empty(result.Values.EnumerateArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Merge_BindingLayerPermission_IsEnforced(bool project)
    {
        ConfigDocument schema = Schema("""{"type":"number","default":5,"allowedLayers":["base","company"]}""");

        ConfigMergeResult result = ConfigMerger.Merge([schema], [], project ? Binding(project: """{"key":{"default":5}}""") : Binding(cabinet: """{"key":{"default":5}}"""), 0);

        Assert.Equal(ConfigMerger.InvalidArgument, result.Reason);
        Assert.Equal(project ? "/projectValues/key" : "/cabinets/0/values/key", result.Location);
    }

    [Fact]
    public void Merge_BindingCanSetKeysFromAllSchemas_AndReturnsSortedKeys()
    {
        ConfigDocument first = Schema() with { Spec = Parse("""{"items":{"z":{"type":"number","allowedLayers":["project"],"required":true}}}""") };
        ConfigDocument second = Schema(pack: "other.motors") with { Spec = Parse("""{"items":{"a":{"type":"boolean","allowedLayers":["cabinet"],"required":true}}}""") };

        ConfigMergeResult result = ConfigMerger.Merge([first, second], [], Binding("""{"z":{"default":5}}""", """{"a":{"default":false}}"""), 0);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(_sortedKeys, result.Values.EnumerateArray().Select(v => v.GetProperty("key").GetString()));
    }

    [Fact]
    public void Merge_IdenticalSchemasFromTwoPacks_DoNotConflictAndKeepBothSources()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema(), Schema(pack: "other.motors")], [], Binding(), 0);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(2, result.Values[0].GetProperty("trace").GetArrayLength());
    }

    [Theory]
    [InlineData("{\"type\":\"number\",\"unit\":\"V\",\"default\":5}")]
    [InlineData("{\"type\":\"number\",\"default\":6}")]
    [InlineData("{\"type\":\"text\",\"default\":\"5\"}")]
    [InlineData("{\"type\":\"number\",\"default\":5,\"required\":false}")]
    public void Merge_SchemasDefineKeyDifferently_ReturnsConflictWithBothSources(string item)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema(), Schema(item, "other.motors")], [], Binding(), 0);

        Assert.Equal(ConfigMerger.ConfigConflict, result.Reason);
        Assert.Equal(2, result.Trace.GetArrayLength());
        Assert.Equal(new[] { Schema().Resource, Schema(pack: "other.motors").Resource }, result.Trace.EnumerateArray().Select(t => t.GetProperty("source").GetProperty("resource").GetString()));
    }

    [Theory]
    [InlineData("{\"type\":\"number\",\"default\":1e1000}", "/spec/items/key/default")]
    [InlineData("{\"type\":\"number\",\"default\":5,\"extra/~\":true}", "/spec/items/key/extra~1~0")]
    public void Merge_InvalidSchema_UsesPackInvalidAndJsonPointer(string item, string location)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema(item)], [], Binding(), 0);

        Assert.Equal(ConfigMerger.PackInvalid, result.Reason);
        Assert.Equal(location, result.Location);
        Assert.Single(result.Trace.EnumerateArray());
    }

    [Theory]
    [InlineData("company", "company")]
    [InlineData("cabinet_type", "company")]
    [InlineData("company", "project")]
    public void Merge_DuplicateReversedOrInvalidPackLayers_IsRefused(string first, string second)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [Layer("{}", first), Layer("{}", second)], Binding(), 0);

        Assert.Equal(ConfigMerger.PackInvalid, result.Reason);
        Assert.Equal("/spec/layer", result.Location);
    }

    [Fact]
    public void Merge_MissingResolvedSchema_IsRefused()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [Layer("{}", pack: "other.motors")], Binding(), 0);

        Assert.Equal(ConfigMerger.PackInvalid, result.Reason);
        Assert.Equal("/spec/schema", result.Location);
    }

    [Theory]
    [InlineData("W", "kW", "79228162514264337593543950335")]
    [InlineData("kW", "W", "0.0000000000000000000000000001")]
    public void Merge_UnitConversionOutsideDecimalRangeOrPrecision_ReturnsProblem(string firstUnit, string secondUnit, string number)
    {
        ConfigDocument first = Schema($$"""{"type":"number","unit":"{{firstUnit}}","default":0}""");
        ConfigDocument second = Schema($$"""{"type":"number","unit":"{{secondUnit}}","default":{{number}}}""", "other.motors");

        ConfigMergeResult result = ConfigMerger.Merge([first, second], [], Binding(), 0);

        Conflict(result, "/spec/items/key/default");
        Assert.Equal(2, result.Trace.GetArrayLength());
    }

    [Fact]
    public void Merge_UnitCannotBeAddedEvenWithoutNumbers_ReturnsConflict()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema("""{"type":"number","required":false}"""), Schema("""{"type":"number","unit":"W","required":false}""", "other.motors")], [], Binding(), 0);

        Conflict(result, "/spec/items/key/unit");
    }

    [Fact]
    public void Merge_FinalCabinetDefaultOutsideConstraint_PointsToTheDefaultAndRetainsBothSources()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [Layer(Setting("""{"fixed":5}"""))], Binding(cabinet: """{"key":{"default":6}}"""), 0);

        Conflict(result, "/cabinets/0/values/key/default");
        Assert.Equal(3, result.Trace.GetArrayLength());
        Assert.Equal(7, result.Trace[2].GetProperty("source").GetProperty("bindingRevision").GetInt32());
    }

    [Fact]
    public void Merge_CabinetConstraintWideningProjectConstraint_IsRejected()
    {
        ProjectBinding binding = Binding("""{"key":{"constraint":{"allowed":[1,5]}}}""", """{"key":{"constraint":{"allowed":[1,5,10]}}}""");

        Conflict(ConfigMerger.Merge([Schema()], [], binding, 0), "/cabinets/0/values/key/constraint");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Merge_InvalidCabinetIndex_IsRefused(int index)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [], Binding(), index);

        Assert.Equal(ConfigMerger.InvalidArgument, result.Reason);
        Assert.Equal("/cabinets", result.Location);
    }

    [Fact]
    public void Merge_SelectsOnlyTheRequestedCabinetValues()
    {
        ProjectBinding binding = Binding() with
        {
            Cabinets =
            [
                new() { Id = "a1", Values = JsonNode.Parse("""{"key":{"default":1}}""")!.AsObject(), Layers = [], Structure = JsonNode.Parse("""{"prefixes":[{"location":"+A1"}]}""")!.AsObject() },
                new() { Id = "a2", Values = JsonNode.Parse("""{"key":{"default":2}}""")!.AsObject(), Layers = [], Structure = JsonNode.Parse("""{"prefixes":[{"location":"+A2"}]}""")!.AsObject() },
            ],
        };

        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [], binding, 1);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(2m, result.Values[0].GetProperty("value").GetDecimal());
        Assert.Equal("/cabinets/1/values/key/default", result.Values[0].GetProperty("trace")[1].GetProperty("pointer").GetString());
    }

    [Theory]
    [InlineData("sample.motors@dev:config_schema/motors", "/source")]
    [InlineData("config_schema/motors", "/source")]
    [InlineData("sample.motors@1.0.0:module/motors", "/source")]
    [InlineData("sample.motors@1.0.0:config_schema/../motors", "/source")]
    public void Merge_NonExactOrWrongSource_IsRefused(string resource, string location)
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema() with { Resource = resource }], [], Binding(), 0);

        Assert.Equal(ConfigMerger.PackInvalid, result.Reason);
        Assert.Equal(location, result.Location);
    }

    [Fact]
    public void Merge_LegacyBinding_IsRefused()
    {
        ConfigMergeResult result = ConfigMerger.Merge([Schema()], [], Binding() with { BindingVersion = 1 }, 0);

        Assert.Equal(ConfigMerger.InvalidArgument, result.Reason);
        Assert.Equal("/bindingVersion", result.Location);
    }

    internal static ConfigDocument Schema(string item = """{"type":"number","default":5}""", string pack = "sample.motors")
    {
        JsonObject node = JsonNode.Parse(item)!.AsObject();
        node.TryAdd("required", true);
        node.TryAdd("allowedLayers", JsonNode.Parse("""["base","company","cabinet_type","project","cabinet"]"""));
        return new(pack + "@1.0.0:config_schema/motors", Manifests.Hash('a'), JsonSerializer.SerializeToElement(new JsonObject { ["items"] = new JsonObject { ["key"] = node } }));
    }

    internal static ConfigDocument Layer(string settings, string layer = "company", string pack = "sample.motors") =>
        new(pack + "@1.0.0:config_layer/" + layer.Replace('_', '-'), Manifests.Hash('b'), Parse($$"""{"layer":"{{layer}}","schema":"config_schema/motors","settings":{{settings}}}"""));

    internal static ProjectBinding Binding(string project = "{}", string cabinet = "{}") => new()
    {
        BindingVersion = 2,
        Project = "plant",
        Revision = 7,
        ProjectValues = JsonNode.Parse(project)!.AsObject(),
        PackRoots = [],
        Cabinets = [new() { Id = "default", Values = JsonNode.Parse(cabinet)!.AsObject(), Layers = [], Structure = new JsonObject { ["all"] = true } }],
    };

    private static string Setting(string constraint) => $$$$"""{"key":{"constraint":{{{{constraint}}}}}}""";

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static void Conflict(ConfigMergeResult result, string location)
    {
        Assert.False(result.Ok);
        Assert.Equal(ConfigMerger.ConfigConflict, result.Reason);
        Assert.Equal(location, result.Location);
        Assert.Empty(result.Values.EnumerateArray());
        Assert.NotEmpty(result.Trace.EnumerateArray());
    }
}
