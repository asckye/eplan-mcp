using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PlanMacroDescriptionTests
{
    [Fact]
    public void Describe_AllFunctions_KeepOrderDuplicatesWiresAndStructureSegments()
    {
        MacroContent content = Content();
        content.Functions = [.. Enumerable.Range(0, 60).Select(_ => Function()),
            new() { Kind = "function", Category = "Wire", Name = "+" },
            new() { Kind = "terminal", Category = "Terminal", Name = "+-X1:?", VisibleName = "-X1" }];
        content.FunctionCount = 62;
        content.ObjectCount = 80;

        PlanMacroInspectData data = Describe(content);

        Assert.Equal(62, data.Functions.Count);
        Assert.Equal(80, data.ObjectCount);
        Assert.All(data.Functions.Take(60), f =>
        {
            Assert.Equal(("function", "Coil", "=+A1-K?1", "-K?1", true), (f.Kind, f.Category, f.Name, f.VisibleName, f.Main));
            Assert.Equal(["=", "+A1"], f.StructureSegments);
        });
        Assert.Equal(("Wire", "+", false), (data.Functions[60].Category, data.Functions[60].Name, data.Functions[60].Main));
        Assert.Equal("terminal", data.Functions[61].Kind);
        Assert.Equal(["+"], data.Functions[61].StructureSegments);
        Assert.Equal((0d, 0d, 52d, -268d), (data.Frame.Left, data.Frame.Top, data.Frame.Right, data.Frame.Bottom));
    }

    [Theory]
    [InlineData(false, 1, 2, "MultiLine", 0)]
    [InlineData(true, 2, 2, "MultiLine", 0)]
    [InlineData(true, 1, 0, "MultiLine", 0)]
    [InlineData(true, 1, 2, "SingleLine", 0)]
    [InlineData(true, 1, 2, "MultiLine", 1)]
    [InlineData(true, 1, 2, "", -1)]
    public void Describe_IncompleteOrWrongSelection_IsCapabilityMissing(bool complete, int count, int objects, string representation, int variant)
    {
        MacroContent content = Content();
        (content.Complete, content.FunctionCount, content.ObjectCount, content.Representation, content.Variant) = (complete, count, objects, representation, variant);
        AssertMissing(content, "macro:complete_variant_0");
    }

    [Theory]
    [InlineData("SingleLine", 0)]
    [InlineData("MultiLine", 1)]
    [InlineData("MultiLine", -1)]
    public void Describe_NoMultiLineVariantZero_IsInvalidArgument(string type, int variant)
    {
        MacroContent content = Content();
        content.Representations = [new(type, variant < 0 ? [] : [variant])];
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Describe(content)).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Single(JArray.Parse(error.Metadata!["violations"]));
        Assert.Null(error.Execution);
    }

    [Theory]
    [InlineData("terminal", "PLCTerminal", "=K1+A1:1.4")]
    [InlineData("terminal", "PLCTerminal", "=K1+A1-KE1:1.4")]
    [InlineData("terminal", "PLCTerminalFluid", "+-K1:1")]
    [InlineData("function", "Coil", "==F+A1-K1")]
    [InlineData("function", "Coil", "+A?-K1")]
    [InlineData("function", "Coil", "=F#S-K1")]
    [InlineData("function", "Coil", "=F=G-K1")]
    [InlineData("function", "Coil", "")]
    [InlineData("unknown", "Coil", "+-K1")]
    [InlineData("function", "Coil", "C:\\private\\SourceProject.elk")]
    public void Describe_UnknownOrExternalStructure_IsCapabilityMissing(string kind, string category, string name)
    {
        MacroContent content = Content();
        content.Functions = [new() { Kind = kind, Category = category, Name = name }];
        AssertMissing(content, "macro:structure_ownership");
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(10, double.NegativeInfinity)]
    [InlineData(-1, 10)]
    public void Describe_InvalidOutline_IsCapabilityMissing(double width, double height)
    {
        MacroContent content = Content();
        (content.Width, content.Height) = (width, height);
        AssertMissing(content, "macro:bounding_frame");
    }

    internal static MacroContent Content() => new()
    {
        Complete = true,
        Representation = "MultiLine",
        Variant = 0,
        Representations = [new("MultiLine", [0, 1, 2])],
        Width = 52,
        Height = 268,
        ObjectCount = 2,
        FunctionCount = 1,
        Functions = [Function()],
    };

    private static MacroFunctionRecord Function() => new() { Kind = "function", Category = "Coil", Name = "=+A1-K?1", VisibleName = "-K?1", Main = true };

    private static PlanMacroInspectData Describe(MacroContent content) => PlanMacroInspectOperation.Describe(content, new string('a', 64));

    private static void AssertMissing(MacroContent content, string capability)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Describe(content)).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
        Assert.Equal(capability, Assert.Single(JArray.Parse(error.Metadata!["missing"])).Value<string>());
        Assert.Null(error.Execution);
        Assert.False(error.Retryable);
        Assert.DoesNotContain("SourceProject", error.Message, StringComparison.Ordinal);
    }
}
