using System.Text.Json;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanDependencyPredictorTests
{
    [Theory]
    [InlineData("en_US", "Power", "en_US", "New", true, "Power")]
    [InlineData("en_US", "Power", "de_DE", "New", true, null)]
    [InlineData("en_US", null, "en_US", "New", true, null)]
    [InlineData("en_US", "Power", "en_US", "Power", false, "Power")]
    [InlineData("en_US", "Power", "de_DE", "", false, null)]
    [InlineData("en_US", "Power", "en_US", "", true, "Power")]
    public async Task PageUpdate_PredictsExactLanguageTypeAndDescriptions(string createdLanguage, string? before, string language,
        string after, bool effective, string? old)
    {
        PlanArgumentStep input = PlanPredictionTestData.Input("page.update", JsonSerializer.Serialize(new
        {
            pages = new[] { new { name = PlanPredictionTestData.Page, description = after } },
            language,
        }));
        PlanStep step = await PlanPredictionTestData.PredictAsync(input: input,
            creation: PlanPredictionTestData.Creation("Overview", before, createdLanguage));
        JsonElement expected = Assert.Single(step.Expected);
        JsonElement value = expected.GetProperty("value");
        Assert.True(expected.GetProperty("prediction").GetBoolean());
        Assert.Equal("Overview", value.GetProperty("pageType").GetString());
        Assert.Equal(language, value.GetProperty("language").GetString());
        Assert.Equal(after.Length == 0 ? null : after, value.GetProperty("description").GetString());
        Assert.Equal(before, value.GetProperty("before").GetProperty("description").GetString());
        Assert.Equal(createdLanguage, value.GetProperty("before").GetProperty("language").GetString());
        Assert.Equal(effective, step.Effective);
        Assert.Empty(step.ModifyScope.Locators);
        Assert.Contains("values/11011/" + language, expected.GetProperty("coveredProperties").EnumerateArray().Select(p => p.GetString()));
        if (effective)
        {
            Assert.Equal(old, Assert.Single(step.PlannedChanges).OldValue);
            Assert.True(step.PlannedChanges[0].Prediction);
        }
        else
        {
            Assert.Empty(step.PlannedChanges);
        }
    }

    [Fact]
    public async Task MacroInsert_PredictsFullInventoryAndExactResourceAndArgumentsWithoutSessionIds()
    {
        PlanStep step = await PlanPredictionTestData.PredictAsync();
        JsonElement expected = Assert.Single(step.Expected);
        JsonElement value = expected.GetProperty("value");
        Assert.Equal(new string('a', 64), value.GetProperty("sha256").GetString());
        Assert.Equal((8, 4), (value.GetProperty("objectCount").GetInt32(), value.GetProperty("functionCount").GetInt32()));
        Assert.Equal("MultiLine", value.GetProperty("representation").GetString());
        Assert.True(JsonElement.DeepEquals(step.Args, value.GetProperty("args")));
        Assert.Equal((0, "keep", 40.5, 280.25), (step.Args.GetProperty("variant").GetInt32(), step.Args.GetProperty("numbering").GetString(),
            step.Args.GetProperty("x").GetDouble(), step.Args.GetProperty("y").GetDouble()));
        Assert.Equal(["function", "function", "terminal", "function"], value.GetProperty("functions").EnumerateArray().Select(f => f.GetProperty("kind").GetString()));
        Assert.Equal("Wire", value.GetProperty("functions")[3].GetProperty("category").GetString());
        Assert.Contains(step.Preconditions, p => p is PlanResourceHash { Sha256: var sha } && sha == new string('a', 64));
        Assert.Equal([PlanPredictionTestData.Page], step.ModifyScope.Pages);
        Assert.Empty(step.ModifyScope.Locators);
        Assert.True(Assert.Single(step.PlannedChanges).Prediction);
        string persisted = PlanCanonical.Serialize(PlanTestData.Definition() with { Steps = [step] });
        Assert.Contains("\"prediction\":true", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint", persisted, StringComparison.OrdinalIgnoreCase);
        string preview = PlanDiffPreview.For([step]);
        Assert.Contains("Prediction:", preview, StringComparison.Ordinal);
        Assert.Contains("namePattern", preview, StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), preview, StringComparison.Ordinal);
        Assert.Contains("graphics", step.Coverage!.Value.GetProperty("unknown").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task MacroInsert_MoreThanFiftyFunctionsAndRepeatedRows_RemainComplete()
    {
        var macro = PlanPredictionTestData.Macro();
        macro.Functions = [.. Enumerable.Range(0, 65).Select(_ => macro.Functions[0])];
        macro.ObjectCount = 70;
        PlanStep step = await PlanPredictionTestData.PredictAsync(macro);
        Assert.Equal(65, Assert.Single(step.Expected).GetProperty("value").GetProperty("functions").GetArrayLength());
    }
}
