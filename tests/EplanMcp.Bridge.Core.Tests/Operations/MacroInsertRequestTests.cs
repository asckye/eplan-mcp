using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The parts of eplan_macro_insert that need no EPLAN and no file (docs/tools/eplan_macro_insert.md, section 7): the
/// outline against the page, the device parts a merge would take, the devices listed and the change's text.
/// </summary>
[Trait("Size", "Small")]
public sealed class MacroInsertRequestTests
{
    // S10's macro, 52 x 268 mm, on S10's sample page, 420 x 297.02 mm.
    private const double Width = 52;
    private const double Height = 268;
    private const double PageWidth = 420;
    private const double PageHeight = 297.02;

    [Theory]
    [InlineData(0, 268)]
    [InlineData(368, 297.02)]
    [InlineData(40, 280)]
    public void Outside_AnOutlineWithinThePage_IsNull(double x, double y)
    {
        Assert.Null(MacroInsertRequest.Outside(x, y, Width, Height, PageWidth, PageHeight));
    }

    [Theory]
    [InlineData(368.5, 280)] // past the right edge
    [InlineData(40, 297.5)] // above the top
    [InlineData(40, 200)] // S10's point: the lower part below the page
    public void Outside_AnOutlinePastAnEdge_GivesTheSizesAndTheRanges(double x, double y)
    {
        string? problem = MacroInsertRequest.Outside(x, y, Width, Height, PageWidth, PageHeight);

        Assert.Equal(
            "'x' and 'y' put the macro's outline, 52 x 268 mm below and right of its top left corner at (x, y), outside the page, 420 x 297.02 mm: x must be from 0 to 368 and y from 268 to 297.02.",
            problem);
    }

    [Fact]
    public void Outside_AMacroLargerThanThePage_SaysSo()
    {
        Assert.Equal("the macro, 52 x 268 mm, is larger than the page, 210 x 148.5 mm.", MacroInsertRequest.Outside(0, 100, Width, Height, 210, 148.5));
    }

    [Theory]
    [InlineData("+-XG8:?", "XG8")]
    [InlineData("=+A1-QA1", "QA1")]
    [InlineData("=GAA+A1-?QA1", "?QA1")]
    [InlineData("=EC1+A3-EC1-MA2", "MA2")]
    [InlineData("+A1-XG9:?1", "XG9")]
    [InlineData("=K1+A1:1.4", null)]
    [InlineData("+-", null)]
    [InlineData("", null)]
    public void DevicePart_IsTheTextAfterTheLastDashBeforeAColon(string tag, string? part)
    {
        Assert.Equal(part, MacroInsertRequest.DevicePart(tag));
    }

    [Fact]
    public void Devices_LeaveOutWires_AndListEachTagOnceWithItsShownTag()
    {
        MacroFunctionRecord[] functions =
        [
            new() { Name = "+A1-XG5:?", Category = "Terminal" },
            new() { Name = "+A1-XG5:?", VisibleName = "-XG5", Category = "Terminal" },
            new() { Name = "+-WG6", VisibleName = ">-WG6", Category = "Cable", Main = true },
            new() { Name = "+-WG6", Category = "Wire" },
            new() { Name = string.Empty, Category = "Terminal" },
            new() { Name = "=+A1-QA1", VisibleName = "-QA1", Category = "NoContact" },
        ];

        Assert.Equal(
            [("+A1-XG5:?", "-XG5", "Terminal"), ("+-WG6", "-WG6", "Cable"), ("=+A1-QA1", "-QA1", "NoContact")],
            MacroInsertRequest.Devices(functions).Select(d => (d.Tag, d.VisibleTag, d.Category)));
    }

    [Theory]
    [InlineData(0, true, 8, "Contactor_Feedback_Control.ema at (40, 200), numbering number, 8 devices")]
    [InlineData(2, false, 1, "Contactor_Feedback_Control.ema at (40, 200), variant 2, numbering keep, 1 device")]
    public void Describe_IsTheChangeTheToolSpecShows(int variant, bool number, int devices, string text)
    {
        Assert.Equal(text, MacroInsertRequest.Describe("EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema", 40, 200, variant, number, devices));
    }

    [Fact]
    public void Merged_IsATagWithAMainFunctionBeforeAndMoreAfter()
    {
        // S10: numbering None inserted the macro's cable again, and the project had +A1-WG6 twice.
        string[] before = ["+A1-WG6", "=GAA+A1-KF1", "=GAA+A1-KF2", "=GAA+A1-KF2", string.Empty];
        string[] after = ["+A1-WG6", "+A1-WG6", "=GAA+A1-KF1", "=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-WG7", "+A1-WG7", string.Empty, string.Empty];

        Assert.Equal(["+A1-WG6"], MacroInsertRequest.Merged(before, after));
    }

    [Fact]
    public void Merged_ADeviceThatHadTwoMainFunctionsAndGetsAThird_IsMerged_AndTagsAreSorted()
    {
        string[] before = ["=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-XG5"];
        string[] after = ["=GAA+A1-KF2", "=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-XG5", "+A1-XG5"];

        Assert.Equal(["+A1-XG5", "=GAA+A1-KF2"], MacroInsertRequest.Merged(before, after));
    }

    [Fact]
    public void Merged_TagsDifferingInCase_AreNotTheSameTag()
    {
        Assert.Empty(MacroInsertRequest.Merged(["+A1-WG6"], ["+A1-WG6", "+a1-wg6"]));
    }

    [Fact]
    public void Describe_KeepsTwoDecimalsOfThePoint()
    {
        Assert.Equal("Top.ema at (40.13, 0.5), numbering number, 0 devices", MacroInsertRequest.Describe("Top.ema", 40.126, 0.5, 0, number: true, 0));
    }
}
