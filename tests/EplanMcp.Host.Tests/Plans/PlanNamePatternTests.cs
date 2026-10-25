using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanNamePatternTests
{
    [Fact]
    public void Visible_StructureQuestionsRemainLiteral()
    {
        var function = new PlanMacroFunction { VisibleName = "=R?+L?-K?" };
        string pattern = PlanNamePattern.Visible(function);
        Assert.True(PlanNamePattern.Matches(pattern, "=R?+L?-K12"));
        Assert.False(PlanNamePattern.Matches(pattern, "=R1+L2-K12"));
    }

    [Theory]
    [InlineData("=F+A1-K?1", true)]
    [InlineData("=F+A1-K231", true)]
    [InlineData("=F+A10-K?1", false)]
    [InlineData("=G+A1-K?1", false)]
    [InlineData("=F+A1-KA1", false)]
    [InlineData("=F+A1-K?2", false)]
    [InlineData("=F+A1-K1\n", false)]
    [InlineData("=F+A1-K+X1", false)]
    [InlineData("=F+A1-K١1", false)]
    public void Generate_OnlyMacroQuestionSegmentsVary(string name, bool matches)
    {
        var function = new PlanMacroFunction { Name = "=OLD+OLD-K?1", StructureSegments = ["=OLD", "+OLD"] };
        string pattern = PlanNamePattern.For("=F+A1&EFS1/3", function);
        Assert.Equal(@"\A=F\+A1-K(?:\?|[0-9]+)1\z", pattern);
        Assert.Equal(matches, PlanNamePattern.Matches(pattern, name));
        Assert.DoesNotContain(".*", pattern, StringComparison.Ordinal);
        Assert.DoesNotContain("\\d", pattern, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=F+A1-K7", true)]
    [InlineData("=F+A1-K8", false)]
    [InlineData("=F+A1-K?", false)]
    [InlineData("=F+A1-K77", false)]
    public void Generate_ExistingNumbersStayLiteral(string name, bool matches)
    {
        var function = new PlanMacroFunction { Name = "=+-K7", StructureSegments = ["=", "+"] };
        Assert.Equal(matches, PlanNamePattern.Matches(PlanNamePattern.For("=F+A1/3", function), name));
    }

    [Fact]
    public void Generate_PageAndMacroRegexCharactersAreEscapedAndStructureIsNeverVariable()
    {
        const string page = "=F?.(1)+A-?/3";
        var function = new PlanMacroFunction { Name = "=+-K.?:1", StructureSegments = ["=", "+"] };
        string pattern = PlanNamePattern.For(page, function);
        Assert.True(PlanNamePattern.Matches(pattern, "=F?.(1)+A-?-K.?:1"));
        Assert.True(PlanNamePattern.Matches(pattern, "=F?.(1)+A-?-K.123:1"));
        Assert.False(PlanNamePattern.Matches(pattern, "=F7.(1)+A-2-K.123:1"));
        Assert.False(PlanNamePattern.Matches(pattern, "=F?.(1)+A-?-Kx123:1"));
    }

    [Fact]
    public void Generate_TerminalsAndWiresUseOnlyTheirRegisteredStructureKinds()
    {
        var terminal = new PlanMacroFunction { Name = "+-X1:?", StructureSegments = ["+"] };
        Assert.Equal("+A1-X1:?", PlanNamePattern.Name("=F+A1&EFS1/3", terminal));
        string pattern = PlanNamePattern.For("=F+A1&EFS1/3", terminal);
        Assert.True(PlanNamePattern.Matches(pattern, "+A1-X1:12"));
        Assert.False(PlanNamePattern.Matches(pattern, "=F+A1-X1:12"));
        var wire = new PlanMacroFunction { Name = "+", StructureSegments = ["+"] };
        Assert.Equal(@"\A\+A1\z", PlanNamePattern.For("=F+A1/3", wire));
    }
}
