using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanPredictionMatchTests
{
    [Fact]
    public async Task Match_OverlappingPatterns_UsesDistinctFunctionsWithoutDependingOnOrder()
    {
        PlanMacroInspectData inspection = PlanPredictionTestData.Macro();
        inspection.Functions =
        [
            new() { Kind = "function", Category = "Coil", Name = "+-K?", Main = true, StructureSegments = ["+"] },
            new() { Kind = "function", Category = "Coil", Name = "+-K1", Main = true, StructureSegments = ["+"] },
        ];
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(inspection);
        List<PlanMacroFunction> functions = PlanPredictionTestData.Functions(inspection);
        functions[0].Name = "+A1-K1";
        functions[1].Name = "+A1-K2";
        Assert.True(PlanPredictionMatch.Matches(predicted, PlanPredictionTestData.RealPlan(predicted), predicted.Args, inspection, functions));
        functions[1].Name = "+A1-K1";
        Assert.True(PlanPredictionMatch.Matches(predicted, PlanPredictionTestData.RealPlan(predicted), predicted.Args, inspection, functions));
        functions[0].Name = "+A1-K2";
        functions[1].Name = "+A1-K2";
        Assert.False(PlanPredictionMatch.Matches(predicted, PlanPredictionTestData.RealPlan(predicted), predicted.Args, inspection, functions));
    }

    [Fact]
    public async Task Match_ExactOrRegisteredDigitsAndReorderedDuplicates_AreAccepted()
    {
        PlanMacroInspectData inspection = PlanPredictionTestData.Macro();
        inspection.Functions.Add(inspection.Functions[0]);
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(inspection);
        WritePlan real = PlanPredictionTestData.RealPlan(predicted);
        List<PlanMacroFunction> functions = PlanPredictionTestData.Functions(inspection);
        Assert.True(PlanPredictionMatch.Matches(predicted, real, predicted.Args, inspection, functions));
        foreach (PlanMacroFunction function in functions)
        {
            function.Name = function.Name.Replace("?", "24", StringComparison.Ordinal);
            function.VisibleName = function.VisibleName.Replace("?", "24", StringComparison.Ordinal);
        }
        functions.Reverse();
        Assert.True(PlanPredictionMatch.Matches(predicted, real, predicted.Args, inspection, functions));
        real.Fingerprint = "changed-runtime-session";
        Assert.True(PlanPredictionMatch.Matches(predicted, real, predicted.Args, inspection, functions));
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("renamed")]
    [InlineData("structure")]
    [InlineData("visible")]
    [InlineData("kind")]
    [InlineData("category")]
    [InlineData("main")]
    [InlineData("duplicate")]
    public async Task Match_AnyFunctionOutsideTheClosedInventory_IsRefused(string fault)
    {
        PlanMacroInspectData inspection = PlanPredictionTestData.Macro();
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(inspection);
        List<PlanMacroFunction> functions = PlanPredictionTestData.Functions(inspection);
        switch (fault)
        {
            case "extra":
                functions.Add(functions[0]);
                break;
            case "missing":
                functions.RemoveAt(0);
                break;
            case "renamed":
                functions[0].Name = "+A1-Q?";
                break;
            case "structure":
                functions[0].Name = "+A10-K?";
                break;
            case "visible":
                functions[0].VisibleName = "-Q?";
                break;
            case "kind":
                functions[0].Kind = "plc";
                break;
            case "category":
                functions[0].Category = "Terminal";
                break;
            case "main":
                functions[0].Main = false;
                break;
            case "duplicate":
                functions[1] = functions[0];
                break;
        }
        Assert.False(PlanPredictionMatch.Matches(predicted, PlanPredictionTestData.RealPlan(predicted), predicted.Args, inspection, functions));
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("inspection-kind")]
    [InlineData("inspection-name")]
    [InlineData("inspection-count")]
    [InlineData("objects")]
    [InlineData("frame")]
    [InlineData("change-extra")]
    [InlineData("change-missing")]
    [InlineData("change-page")]
    [InlineData("change-action")]
    [InlineData("change-value")]
    [InlineData("plan-macro")]
    [InlineData("plan-objects")]
    [InlineData("project")]
    [InlineData("copy")]
    [InlineData("master-data")]
    public async Task Match_ChangedResourceDescriptionOrRealPlan_IsRefused(string fault)
    {
        PlanMacroInspectData inspection = PlanPredictionTestData.Macro();
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(inspection);
        WritePlan real = PlanPredictionTestData.RealPlan(predicted);
        List<PlanMacroFunction> functions = PlanPredictionTestData.Functions(inspection);
        switch (fault)
        {
            case "sha":
                inspection.Sha256 = new string('b', 64);
                break;
            case "inspection-kind":
                inspection.Functions[0].Kind = "terminal";
                break;
            case "inspection-name":
                inspection.Functions[0].Name = "+-Q?";
                break;
            case "inspection-count":
                inspection.Functions.RemoveAt(0);
                break;
            case "objects":
                inspection.ObjectCount++;
                break;
            case "frame":
                inspection.Frame.Right++;
                break;
            case "change-extra":
                real.Changes.Add(real.Changes[0]);
                break;
            case "change-missing":
                real.Changes.Clear();
                break;
            case "change-page":
                real.Changes[0].Name = "+A10/3";
                break;
            case "change-action":
                real.Changes[0].Action = "delete";
                break;
            case "change-value":
                real.Changes[0].NewValue = "Other.ema at (40.5, 280.25), numbering keep, 3 devices";
                break;
            case "plan-macro":
                real.Details!["macro"] = "Other.ema";
                break;
            case "plan-objects":
                real.Details!["objectCount"] = "9";
                break;
            case "project":
                real.Project = "Other";
                break;
            case "copy":
                real.ProjectPath += "-copy";
                break;
            case "master-data":
                real.MasterData = new();
                break;
        }
        Assert.False(PlanPredictionMatch.Matches(predicted, real, predicted.Args, inspection, functions));
    }

    [Theory]
    [InlineData("page", "\"+A10/3\"")]
    [InlineData("macro", "\"Other.ema\"")]
    [InlineData("x", "40.501")]
    [InlineData("y", "280.251")]
    [InlineData("variant", "1")]
    [InlineData("numbering", "\"number\"")]
    public async Task Match_ExactInsertionArgumentChanges_AreRefused(string key, string json)
    {
        PlanMacroInspectData inspection = PlanPredictionTestData.Macro();
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(inspection);
        JsonNode args = JsonNode.Parse(predicted.Args.GetRawText())!;
        args[key] = JsonNode.Parse(json);
        Assert.False(PlanPredictionMatch.Matches(predicted, PlanPredictionTestData.RealPlan(predicted),
            System.Text.Json.JsonSerializer.SerializeToElement(args), inspection, PlanPredictionTestData.Functions(inspection)));
    }

    [Fact]
    public async Task Match_MissingCompleteInspectionOrFunctionInventory_IsRefused()
    {
        PlanStep predicted = await PlanPredictionTestData.PredictAsync();
        WritePlan real = PlanPredictionTestData.RealPlan(predicted);
        Assert.False(PlanPredictionMatch.Matches(predicted, real, predicted.Args));
        Assert.False(PlanPredictionMatch.Matches(predicted, real, predicted.Args, PlanPredictionTestData.Macro()));
        Assert.False(PlanPredictionMatch.Matches(predicted with { Prediction = null }, real, predicted.Args));
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("extra", false)]
    [InlineData("missing", false)]
    [InlineData("old", false)]
    [InlineData("new", false)]
    [InlineData("language", false)]
    public async Task Match_PageUpdate_RequiresTheExactApprovedChanges(string fault, bool matches)
    {
        PlanStep predicted = await PlanPredictionTestData.PredictAsync(input: PlanPredictionTestData.Input("page.update"));
        WritePlan real = PlanPredictionTestData.RealPlan(predicted);
        switch (fault)
        {
            case "extra":
                real.Changes.Add(real.Changes[0]);
                break;
            case "missing":
                real.Changes.Clear();
                break;
            case "old":
                real.Changes[0].OldValue = "Unexpected";
                break;
            case "new":
                real.Changes[0].NewValue = "Unexpected";
                break;
            case "language":
                real.Changes[0].Property = "description (de_DE)";
                break;
        }
        Assert.Equal(matches, PlanPredictionMatch.Matches(predicted, real, predicted.Args));
    }
}
