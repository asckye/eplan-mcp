using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class WorkflowValidatorTests
{
    [Fact]
    public void Expand_ParametersAndConfig_PreservesTypesDefaultsAndTemplate()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![0]!["args"]!["pages"]![0]!["description"] = JsonNode.Parse("""{"config":"motor.starter"}""");
        string original = spec.ToJsonString();
        WorkflowDefinition workflow = WorkflowFixtures.Validate(spec);

        var steps = WorkflowValidator.Expand(workflow, WorkflowFixtures.Element("""{"x":12.5}"""), WorkflowFixtures.Element("""{"motor.starter":"star_delta"}"""));

        Assert.Equal("star_delta", steps[0].Args.GetProperty("pages")[0].GetProperty("description").GetString());
        Assert.Equal(12.5m, steps[1].Args.GetProperty("x").GetDecimal());
        Assert.Equal([1], steps[1].DependsOn);
        Assert.Equal(original, spec.ToJsonString());
        Assert.Equal(40m, WorkflowValidator.Expand(workflow, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("""{"motor.starter":"dol"}"""))[1].Args.GetProperty("x").GetDecimal());
    }

    [Theory]
    [InlineData("dol", 2, 200)]
    [InlineData("star_delta", 3, 250)]
    public void Expand_DeclaredBranches_SelectsOneCaseAndRemapsDependencies(string mode, int count, int y)
    {
        WorkflowDefinition workflow = WorkflowFixtures.Validate(WorkflowFixtures.BranchSpec());

        var steps = WorkflowValidator.Expand(workflow, JsonSerializer.SerializeToElement(new { mode }), WorkflowFixtures.Element("{}"));

        Assert.Equal(count, steps.Length);
        Assert.Equal(y, steps[1].Args.GetProperty("y").GetInt32());
        if (mode == "star_delta")
        {
            Assert.Equal([1, 2], steps[2].DependsOn);
        }
    }

    [Fact]
    public void Expand_ConfigBooleanBranch_UsesResolvedValue()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["branches"] = JsonNode.Parse("""{"enabled":{"config":"motor.enabled"}}""");
        spec["steps"]![1]!["branch"] = "enabled";
        spec["steps"]![1]!["case"] = true;
        WorkflowDefinition workflow = WorkflowFixtures.Validate(spec);

        Assert.Single(WorkflowValidator.Expand(workflow, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("""{"motor.enabled":false}""")));
        Assert.Equal(2, WorkflowValidator.Expand(workflow, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("""{"motor.enabled":true}""")).Length);
        Assert.StartsWith("/spec/steps/1/branch:", Assert.Throws<PackFormatException>(() => WorkflowValidator.Expand(workflow, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("{}"))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("include")]
    [InlineData("extends")]
    [InlineData("loops")]
    public void Validate_RecursiveOrDynamicMembers_AreRejected(string member)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec[member] = "workflow/self";

        Assert.StartsWith("/spec/" + member + ":", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workflow.run")]
    [InlineData("action")]
    [InlineData("module.insert")]
    [InlineData("placeholder.apply")]
    [InlineData("create")]
    public void Validate_OperationsOutsideClosedCatalog_AreRejected(string op)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![0]!["op"] = op;

        Assert.StartsWith("/spec/steps/0/op:", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Validate_StepLimit_CountsAllSteps(int count, bool valid)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        JsonNode step = spec["steps"]![0]!.DeepClone();
        spec["steps"] = new JsonArray(Enumerable.Range(0, count).Select(_ => step.DeepClone()).ToArray());
        if (valid)
        {
            Assert.NotNull(WorkflowFixtures.Validate(spec));
        }
        else
        {
            Assert.StartsWith("/spec/steps:", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("[2]")]
    [InlineData("[3]")]
    [InlineData("[1,1]")]
    [InlineData("[\"1\"]")]
    [InlineData("[1.5]")]
    public void Validate_RecursiveForwardOrMalformedDependencies_AreRejected(string dependencies)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![1]!["dependsOn"] = JsonNode.Parse(dependencies);

        Assert.StartsWith("/spec/steps/1/dependsOn:", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DependencyAcrossMutuallyExclusiveCases_IsRejected()
    {
        JsonObject spec = WorkflowFixtures.BranchSpec();
        spec["steps"]![2]!["dependsOn"] = new JsonArray(2);

        Assert.Contains("cannot depend on another branch", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("branch", "\"undeclared\"", "/spec/steps/1/branch")]
    [InlineData("case", "\"other\"", "/spec/steps/1/case")]
    [InlineData("branch", "{\"parameter\":\"mode\"}", "/spec/steps/1/branch")]
    public void Validate_BranchesMustBeDeclaredAndTyped(string member, string value, string expectedPointer)
    {
        JsonObject spec = WorkflowFixtures.BranchSpec();
        spec["steps"]![1]![member] = JsonNode.Parse(value);

        Assert.StartsWith(expectedPointer + ":", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("macro", "{\"parameter\":\"label\"}")]
    [InlineData("x", "{\"parameter\":\"label\"}")]
    [InlineData("x", "{\"config\":\"motor.power\"}")]
    [InlineData("x", "{\"parameter\":\"missing\"}")]
    [InlineData("x", "{\"parameter\":\"x\",\"config\":\"motor.power\"}")]
    [InlineData("project", "\"external\"")]
    [InlineData("cabinet", "\"a2\"")]
    [InlineData("tier", "\"Read\"")]
    [InlineData("approval", "true")]
    [InlineData("variant", "1")]
    [InlineData("numbering", "\"number\"")]
    public void Validate_UnsafeSubstitutionOrArguments_AreRejected(string field, string value)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"]![1]!["args"]![field] = JsonNode.Parse(value);

        Assert.StartsWith("/spec/steps/1/args/" + field + ":", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"x\":\"40\"}", "/inputs/x")]
    [InlineData("{\"x\":101}", "/inputs/x")]
    [InlineData("{\"mode\":\"other\"}", "/inputs/mode")]
    [InlineData("{\"unknown\":true}", "/inputs/unknown")]
    [InlineData("{\"x\":1e100}", "/inputs/x")]
    [InlineData("null", "/inputs")]
    public void Expand_InvalidInputs_AreRejectedWithoutCoercion(string inputs, string expectedPointer)
    {
        WorkflowDefinition workflow = WorkflowFixtures.Validate();

        Assert.StartsWith(expectedPointer + ":", Assert.Throws<PackFormatException>(() => WorkflowValidator.Expand(workflow, WorkflowFixtures.Element(inputs), WorkflowFixtures.Element("{}"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_MissingRequiredOrReferencedValues_AreRejected()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["parameters"]!["label"]!.AsObject().Remove("default");
        WorkflowDefinition optional = WorkflowFixtures.Validate(spec);
        Assert.StartsWith("/spec/steps/0/args/pages/0/description:", Assert.Throws<PackFormatException>(() => WorkflowValidator.Expand(optional, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("{}"))).Message, StringComparison.Ordinal);
        spec["parameters"]!["label"]!["required"] = true;
        WorkflowDefinition required = WorkflowFixtures.Validate(spec);
        Assert.StartsWith("/inputs/label:", Assert.Throws<PackFormatException>(() => WorkflowValidator.Expand(required, WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("{}"))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ParameterUnitAndBounds_ReuseConfigValidationPointers()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["parameters"]!["x"]!["unit"] = "unknown";

        Assert.StartsWith("/spec/parameters/x/unit:", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_SubstitutionSize_IsBoundedAfterDuplication()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        JsonNode step = spec["steps"]![0]!.DeepClone();
        spec["steps"] = new JsonArray(Enumerable.Range(0, 20).Select(_ => step.DeepClone()).ToArray());
        WorkflowDefinition workflow = WorkflowFixtures.Validate(spec);

        Assert.StartsWith("/spec/steps:", Assert.Throws<PackFormatException>(() => WorkflowValidator.Expand(workflow,
            JsonSerializer.SerializeToElement(new { label = new string('a', 14_000) }), WorkflowFixtures.Element("{}"))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"eq\":[{\"parameter\":\"mode\"},{\"config\":\"motor.starter\"}]}", true)]
    [InlineData("{\"eq\":[{\"parameter\":\"missing\"},{\"literal\":true}]}", false)]
    [InlineData("{\"literal\":42}", false)]
    public void Validate_Applicability_ReusesTypedPredicates(string predicate, bool valid)
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["applicability"] = JsonNode.Parse(predicate);
        if (valid)
        {
            Assert.NotNull(WorkflowFixtures.Validate(spec));
        }
        else
        {
            Assert.StartsWith("/spec/applicability", Assert.Throws<PackFormatException>(() => WorkflowFixtures.Validate(spec)).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Validate_AllSevenOperations_AcceptClosedArguments()
    {
        JsonObject spec = WorkflowFixtures.Spec();
        spec["steps"] = JsonNode.Parse("""
            [{"op":"page.create","args":{"pages":[{"name":"=AP+A1/1"}]}},
             {"op":"page.update","args":{"pages":[{"name":"=AP+A1/1","description":"Motor"}]}},
             {"op":"device.update","args":{"tag":"-Q1","new_tag":"-Q2"}},
             {"op":"macro.insert","args":{"page":"=AP+A1/1","macro":"macro/motor.ema","x":40,"y":200,"variant":0,"numbering":"keep"}},
             {"op":"page.delete","args":{"pages":["=AP+A1/1"]}},
             {"op":"page.renumber","args":{"pages":["=AP+A1/1"],"start":2,"step":1,"keep_text":false,"subpages":"retain"}},
             {"op":"device.renumber","args":{"pages":["=AP+A1/1"],"only_unnumbered":true,"scheme":"IEC","start":1,"step":1}}]
            """);

        Assert.Equal(7, WorkflowValidator.Expand(WorkflowFixtures.Validate(spec), WorkflowFixtures.Element("{}"), WorkflowFixtures.Element("{}")).Length);
    }
}
