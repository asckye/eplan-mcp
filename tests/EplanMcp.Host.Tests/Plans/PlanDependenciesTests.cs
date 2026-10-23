using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanDependenciesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Dependencies_RejectInvalidSelfAndForwardNumbers(int dependency)
    {
        PlanArgumentCheck result = PlanArgumentsTests.Check(
            PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }),
            PlanArgumentsTests.Step("page.update", new { pages = PlanArgumentsTests.Pages(1) }, dependency));
        Assert.Equal("/steps/1/depends_on/0", Assert.Single(result.Violations).Pointer);
    }

    [Fact]
    public void DuplicateDependencies_AreRejectedAtTheDuplicate()
    {
        PlanArgumentCheck result = PlanArgumentsTests.Check(
            PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }),
            PlanArgumentsTests.Step("page.update", new { pages = PlanArgumentsTests.Pages(1) }, 1, 1));
        Assert.Equal("/steps/1/depends_on/1", Assert.Single(result.Violations).Pointer);
    }

    [Theory]
    [InlineData("null", "/steps/0/depends_on")]
    [InlineData("{}", "/steps/0/depends_on")]
    [InlineData("[\"1\"]", "/steps/0/depends_on/0")]
    [InlineData("[1.5]", "/steps/0/depends_on/0")]
    public void DependencyShape_IsStrict(string depends, string expectedPointer)
    {
        string json = "{\"project\":\"Demo\",\"steps\":[{\"op\":\"page.delete\",\"args\":{\"pages\":[\"+A/1\"]},\"depends_on\":" + depends + "}]}";
        Assert.Contains(PlanArguments.Check(PlanTestData.Json(json), "en_US", "IEC").Violations, v => v.Pointer == expectedPointer);
    }

    [Theory]
    [InlineData("page.update")]
    [InlineData("macro.insert")]
    public void PageReference_ResolvesNamesAndInfersAnEarlierDependency(string op)
    {
        var reference = new { from_step = 1, page = "+A/1" };
        object args = op == "page.update" ? new { pages = new[] { new { name = reference, description = "New" } } }
            : new { page = reference, macro = "Motor.ema", x = 40, y = 100 };
        PlanArgumentCheck result = PlanArgumentsTests.Check(
            PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }), PlanArgumentsTests.Step(op, args));
        Assert.Empty(result.Violations);
        Assert.Equal([1], result.Steps[1].DependsOn);
        JsonElement normalized = result.Steps[1].Args;
        Assert.Equal("+A/1", op == "page.update" ? normalized.GetProperty("pages")[0].GetProperty("name").GetString() : normalized.GetProperty("page").GetString());
        Assert.DoesNotContain("from_step", normalized.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "+A/1")]
    [InlineData(2, "+A/1")]
    [InlineData(3, "+A/1")]
    [InlineData(1, "+A/2")]
    [InlineData(1, "+a/1")]
    public void PageReference_RequiresExactUniquePageFromEarlierCreation(int source, string page)
    {
        PlanArgumentCheck result = PlanArgumentsTests.Check(
            PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }),
            PlanArgumentsTests.Step("macro.insert", new { page = new { from_step = source, page }, macro = "Motor.ema", x = 40, y = 100 }));
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/1/args/page");
    }

    [Fact]
    public void ReferenceToNonCreateDuplicatePageOrWrongSlot_IsRejected()
    {
        foreach (string source in new[] { "page.update", "page.create" })
        {
            object[] pages = source == "page.create" ? [new { name = "+A/1" }, new { name = "+A/1" }] : PlanArgumentsTests.Pages(1);
            Assert.Contains(PlanArgumentsTests.Check(PlanArgumentsTests.Step(source, new { pages }),
                PlanArgumentsTests.Step("macro.insert", new { page = new { from_step = 1, page = "+A/1" }, macro = "Motor.ema", x = 1, y = 2 })).Violations,
                v => v.Pointer == "/steps/1/args/page");
        }
        Assert.Contains(PlanArgumentsTests.Check(PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }),
            PlanArgumentsTests.Step("device.update", new { tag = new { from_step = 1, page = "+A/1" }, new_tag = "-K2" })).Violations,
            v => v.Pointer == "/steps/1/args/tag");
        Assert.Contains(PlanArgumentsTests.Check(PlanArgumentsTests.Step("page.create", new { pages = PlanArgumentsTests.Pages(1) }),
            PlanArgumentsTests.Step("macro.insert", new { page = new { from_step = 1, page = "+A/1", extra = true }, macro = "Motor.ema", x = 1, y = 2 })).Violations,
            v => v.Pointer == "/steps/1/args/page");
    }

    [Theory]
    [InlineData("page.update")]
    [InlineData("macro.insert")]
    public void OnlyTwoPredictions_AreAdmittedAfterResolution(string op)
    {
        PlanStep create = Resolved(1, "page.create", "+A/1");
        PlanStep child = Resolved(2, op, "+A/1", [1]);
        Assert.Empty(PlanDependencies.Check([create, child], new Dictionary<int, PlanMacroTarget> { [2] = new("Circuit", true, "MultiLine") }));
        Assert.Contains(PlanDependencies.Check([create, child with { DependsOn = [] }], new Dictionary<int, PlanMacroTarget> { [2] = new("Circuit", true, "MultiLine") }),
            v => v.Message.Contains("no predictor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("page.update", "page.update")]
    [InlineData("page.update", "macro.insert")]
    [InlineData("macro.insert", "device.update")]
    [InlineData("macro.insert", "macro.insert")]
    [InlineData("device.update", "page.delete")]
    [InlineData("page.create", "page.delete")]
    [InlineData("page.create", "device.renumber")]
    public void OtherReadModifyOverlaps_HaveNoPredictor(string earlier, string later)
    {
        ImmutableArray<PlanViolation> violations = PlanDependencies.Check([Resolved(1, earlier, "+A/1"), Resolved(2, later, "+A/1", [1])],
            new Dictionary<int, PlanMacroTarget> { [1] = new("Circuit", true, "MultiLine"), [2] = new("Circuit", true, "MultiLine") });
        Assert.Contains(violations, v => v.Pointer == "/steps/1/args" && v.Message.Contains("no predictor", StringComparison.Ordinal));
    }

    [Fact]
    public void DisjointExplicitDependencies_AreAllowed()
    { Assert.Empty(PlanDependencies.Check([Resolved(1, "page.update", "+A/1"), Resolved(2, "page.update", "+A/2", [1])], new Dictionary<int, PlanMacroTarget>())); }

    [Fact]
    public void PartialCreationCoverage_DoesNotPredictOtherPages()
    {
        PlanStep update = Resolved(2, "page.update", "+A/1", [1]) with { Args = PlanTestData.Json("""{"pages":[{"name":"+A/1"},{"name":"+A/2"}]}""") };
        Assert.Contains(PlanDependencies.Check([Resolved(1, "page.create", "+A/1"), update], new Dictionary<int, PlanMacroTarget>()),
            v => v.Message.Contains("no predictor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Renumbering_MustBeTheLastEffectiveStep(string op)
    {
        PlanStep renumber = Resolved(1, op, "+A/1"), following = Resolved(2, "page.update", "+A/2");
        Assert.Contains(PlanDependencies.Check([renumber, following], new Dictionary<int, PlanMacroTarget>()), v => v.Pointer == "/steps/0/op");
        Assert.Empty(PlanDependencies.Check([renumber, following with { Effective = false }], new Dictionary<int, PlanMacroTarget>()));
        Assert.Empty(PlanDependencies.Check([renumber with { Effective = false }, following], new Dictionary<int, PlanMacroTarget>()));
    }

    [Theory]
    [InlineData("Circuit", true, "MultiLine", true)]
    [InlineData("Graphics", true, "MultiLine", false)]
    [InlineData("CircuitSingleLine", true, "MultiLine", false)]
    [InlineData("Circuit", false, "MultiLine", false)]
    [InlineData("Circuit", true, "SingleLine", false)]
    public void MacroTarget_RequiresEmptyCircuitAndMultiLine(string type, bool empty, string representation, bool valid)
    {
        ImmutableArray<PlanViolation> result = PlanDependencies.Check([Resolved(1, "macro.insert", "+A/1")], new Dictionary<int, PlanMacroTarget> { [1] = new(type, empty, representation) });
        Assert.Equal(valid, result.IsEmpty);
        if (!valid)
        { Assert.Equal("/steps/0/args/page", Assert.Single(result).Pointer); }
    }

    [Fact]
    public void MissingMacroEvidenceAndWideImpact_AreRefused()
    {
        PlanStep step = Resolved(1, "macro.insert", "+A/1") with { ModifyScope = PlanScopeRules.Pages(Enumerable.Range(1, 51).Select(i => "+A/" + i)) };
        ImmutableArray<PlanViolation> errors = PlanDependencies.Check([step], new Dictionary<int, PlanMacroTarget>());
        Assert.Contains(errors, v => v.Pointer == "/steps/0/args/pages");
        Assert.Contains(errors, v => v.Pointer == "/steps/0/args/page");
    }

    private static PlanStep Resolved(int index, string op, string page, ImmutableArray<int> dependencies = default)
    {
        JsonElement args = JsonSerializer.SerializeToElement(op == "macro.insert" ? (object)new { page } : new { pages = new[] { new { name = page } } });
        PlanScope scope = PlanScopeRules.Pages([page]);
        return new(index, op, args, PlanStepCatalog.Tier(op), scope, scope, [], [], [], dependencies.IsDefault ? [] : dependencies, null, true);
    }
}
