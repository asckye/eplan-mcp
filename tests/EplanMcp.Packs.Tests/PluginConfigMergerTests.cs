using System.Text.Json;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PluginConfigMergerTests
{
    [Fact]
    public void GlobalDefaults_PrecedeTheExistingCompanyChain()
    {
        ConfigMergeResult result = ConfigMerger.Merge(
            [ConfigMergerTests.Schema(), ConfigMergerTests.Schema(pack: "plugin.first")],
            [ConfigMergerTests.Layer("""{"key":{"default":9}}""")], ConfigMergerTests.Binding(), 0,
            [ConfigMergerTests.Layer("""{"key":{"default":7}}""", pack: "plugin.first")]);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(9, result.Values[0].GetProperty("value").GetInt32());
        JsonElement[] defaults = [.. result.Values[0].GetProperty("trace").EnumerateArray().Where(t => t.GetProperty("kind").GetString() == "default")];
        Assert.Equal("plugin.first@1.0.0", defaults[^2].GetProperty("plugin").GetString());
        Assert.Equal("global", defaults[^2].GetProperty("scope").GetString());
        Assert.False(defaults[^1].TryGetProperty("scope", out _));
    }

    [Fact]
    public void LaterGlobalPlugin_WinsEvenWhenItsEngineeringLayerIsEarlier()
    {
        ConfigMergeResult result = ConfigMerger.Merge(
            [ConfigMergerTests.Schema(pack: "plugin.first"), ConfigMergerTests.Schema(pack: "plugin.second")],
            [], ConfigMergerTests.Binding(), 0,
            [ConfigMergerTests.Layer("""{"key":{"default":7}}""", "cabinet_type", "plugin.first"),
             ConfigMergerTests.Layer("""{"key":{"default":8}}""", pack: "plugin.second")]);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(8, result.Values[0].GetProperty("value").GetInt32());
        Assert.Equal(["plugin.first@1.0.0", "plugin.second@1.0.0"], result.Values[0].GetProperty("trace").EnumerateArray()
            .Where(t => t.TryGetProperty("plugin", out _)).Select(t => t.GetProperty("plugin").GetString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectAndCabinetValues_StillWin(bool project)
    {
        ProjectBinding binding = project ? ConfigMergerTests.Binding(project: """{"key":{"default":10}}""")
            : ConfigMergerTests.Binding(cabinet: """{"key":{"default":10}}""");
        ConfigMergeResult result = ConfigMerger.Merge([ConfigMergerTests.Schema()], [], binding, 0,
            [ConfigMergerTests.Layer("""{"key":{"default":7}}""")]);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(10, result.Values[0].GetProperty("value").GetInt32());
        Assert.Equal(project ? "project" : "cabinet", result.Values[0].GetProperty("trace").EnumerateArray().Last().GetProperty("layer").GetString());
    }

    [Fact]
    public void ACompanyConstraint_CannotWidenAGlobalConstraint()
    {
        ConfigMergeResult result = ConfigMerger.Merge([ConfigMergerTests.Schema()],
            [ConfigMergerTests.Layer("""{"key":{"constraint":{"allowed":[1,5,10]}}}""", "cabinet_type")],
            ConfigMergerTests.Binding(), 0, [ConfigMergerTests.Layer("""{"key":{"constraint":{"allowed":[1,5]}}}""")]);

        Assert.False(result.Ok);
        Assert.Equal(ConfigMerger.ConfigConflict, result.Reason);
        Assert.Equal("/spec/settings/key/constraint", result.Location);
        Assert.Contains(result.Trace.EnumerateArray(), t => t.TryGetProperty("scope", out JsonElement scope) && scope.GetString() == "global");
    }

    [Fact]
    public void AGlobalLayer_StillUsesItsSchemaLayerPermission()
    {
        ConfigDocument schema = ConfigMergerTests.Schema("""{"type":"number","default":5,"allowedLayers":["base"]}""");
        ConfigMergeResult result = ConfigMerger.Merge([schema], [], ConfigMergerTests.Binding(), 0,
            [ConfigMergerTests.Layer("""{"key":{"default":7}}""")]);

        Assert.False(result.Ok);
        Assert.Equal("/spec/settings/key", result.Location);
        Assert.Equal("global", result.Trace[0].GetProperty("scope").GetString());
    }

    [Fact]
    public void BindingLock_SkipsEveryGlobalLayerForThatId_AndReportsOneTraceWarning()
    {
        ProjectBinding binding = ConfigMergerTests.Binding() with
        {
            Locks = [new() { Id = "sample.motors", Version = "2.0.0", Digest = Manifests.Hash('c') }]
        };
        ConfigMergeResult result = ConfigMerger.Merge([ConfigMergerTests.Schema()], [], binding, 0,
            [ConfigMergerTests.Layer("""{"key":{"default":7}}"""), ConfigMergerTests.Layer("""{"key":{"default":8}}""", "cabinet_type")]);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(5, result.Values[0].GetProperty("value").GetInt32());
        JsonElement warning = Assert.Single(result.Trace.EnumerateArray());
        Assert.Equal("warning", warning.GetProperty("kind").GetString());
        Assert.Equal("global", warning.GetProperty("scope").GetString());
        Assert.Equal("sample.motors@1.0.0", warning.GetProperty("plugin").GetString());
    }

    [Fact]
    public void GlobalMerge_DoesNotMutateInputs_AndEmptyGroupPreservesTheOldResult()
    {
        ConfigDocument[] schemas = [ConfigMergerTests.Schema()];
        ConfigDocument[] globals = [ConfigMergerTests.Layer("""{"key":{"default":7}}""")];
        ProjectBinding binding = ConfigMergerTests.Binding(cabinet: """{"key":{"default":10}}""");
        byte[] before = PackJson.Serialize(new { schemas, globals, binding });

        Assert.True(ConfigMerger.Merge(schemas, [], binding, 0, globals).Ok);
        Assert.Equal(before, PackJson.Serialize(new { schemas, globals, binding }));
        Assert.Equal(PackJson.Serialize(ConfigMerger.Merge(schemas, [], binding, 0)),
            PackJson.Serialize(ConfigMerger.Merge(schemas, [], binding, 0, [])));
    }
}
