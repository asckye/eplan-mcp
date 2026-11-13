using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

[Trait("Size", "Small")]
public sealed class PageExportComparerMacroShiftTests
{
    private const double Dx = -0.109782, Dy = -0.23177;

    /// <summary>A page of horizontal lines 10 mm apart, each 20 mm long, optionally translated.</summary>
    private static string Page(int lines, double dx = 0, double dy = 0, double stretchLast = 0)
    {
        var placements = new JArray();
        for (int index = 0; index < lines; index++)
        {
            double x = 10 + dx, y = 20 + index * 10 + dy;
            double length = 20 + (index == lines - 1 ? stretchLast : 0);
            placements.Add(new JObject
            {
                ["type"] = "Line",
                ["oid"] = index + 1,
                ["location"] = Point(x, y),
                ["start"] = Point(x, y),
                ["end"] = Point(x + length, y),
                ["boundingBox"] = new JArray(Point(x, y), Point(x + length, y)),
            });
        }
        return new JObject { ["name"] = "+A1/1", ["pageType"] = "Circuit", ["placements"] = placements, ["connections"] = new JArray() }
            .ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };

    [Fact]
    public void ConstantOffsetOnManyGraphics_IsAMacroVersionDifferenceOutsideTheRate()
    {
        PageExportComparison result = PageExportComparer.Compare(Page(12), Page(12, Dx, Dy));

        Assert.Equal(12, result.MacroShiftDifferences.Count);
        Assert.Empty(result.Types["Line"].Missing);
        Assert.Empty(result.Types["Line"].Extra);
        Assert.All(result.MacroShiftDifferences, shift =>
        {
            Assert.Equal(Dx, shift.X, 6);
            Assert.Equal(Dy, shift.Y, 6);
            Assert.Equal("Line", shift.Type);
        });
    }

    [Fact]
    public void TooFewSharingTheOffset_StayMissingAndExtra()
    {
        PageExportComparison result = PageExportComparer.Compare(Page(PageExportComparerMacroShift.MinimumObjects - 1),
            Page(PageExportComparerMacroShift.MinimumObjects - 1, Dx, Dy));

        Assert.Empty(result.MacroShiftDifferences);
        Assert.Equal(PageExportComparerMacroShift.MinimumObjects - 1, result.Types["Line"].Missing.Count);
        Assert.Equal(PageExportComparerMacroShift.MinimumObjects - 1, result.Types["Line"].Extra.Count);
    }

    [Fact]
    public void AGraphicWhoseShapeAlsoChanged_IsNotExcused()
    {
        PageExportComparison result = PageExportComparer.Compare(Page(12), Page(12, Dx, Dy, stretchLast: 5));

        Assert.Equal(11, result.MacroShiftDifferences.Count);
        Assert.Equal(1, Assert.Single(result.Types["Line"].Missing)["oid"]);
        Assert.Equal(12, Assert.Single(result.Types["Line"].Extra)["oid"]);
    }

    [Fact]
    public void IdenticalPages_ReportNoOffset()
    {
        PageExportComparison result = PageExportComparer.Compare(Page(12), Page(12));

        Assert.Empty(result.MacroShiftDifferences);
        Assert.Equal(12, result.Types["Line"].Matched.Count);
    }
}
