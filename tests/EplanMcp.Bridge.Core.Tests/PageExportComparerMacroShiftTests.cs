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

    /// <summary>Arcs at the same macro translation whose stored angles differ: the owner's rule of 2026-11-13.</summary>
    private static string ArcPage(int arcs, double dx = 0, double dy = 0, double endAngle = 0, double radius = 2)
    {
        var placements = new JArray();
        for (int index = 0; index < arcs; index++)
        {
            double x = 10 + dx, y = 20 + index * 10 + dy;
            placements.Add(new JObject
            {
                ["type"] = "Arc",
                ["oid"] = index + 1,
                ["location"] = Point(x, y),
                ["center"] = Point(x, y),
                ["radius"] = radius,
                ["startAngle"] = 0.0,
                ["endAngle"] = endAngle,
                ["boundingBox"] = new JArray(Point(x - radius, y - radius), Point(x + radius, y + radius)),
            });
        }
        return new JObject { ["name"] = "+A1/1", ["pageType"] = "Circuit", ["placements"] = placements, ["connections"] = new JArray() }
            .ToString(Newtonsoft.Json.Formatting.None);
    }

    [Fact]
    public void AnglesStatedDifferentlyAtTheMacroOffset_AreAlsoAMacroVersionDifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Page(12) + "", Page(12, Dx, Dy));
        Assert.Equal(12, result.MacroShiftDifferences.Count);

        // Twelve lines establish the translation; the arcs sit at the same one but state their end angle differently.
        string source = Merge(Page(12), ArcPage(4));
        string target = Merge(Page(12, Dx, Dy), ArcPage(4, Dx, Dy, endAngle: 1.5707963267948966));
        result = PageExportComparer.Compare(source, target);

        Assert.Empty(result.Types["Arc"].Missing);
        Assert.Empty(result.Types["Arc"].Extra);
        Assert.Equal(4, result.MacroShiftDifferences.Count(shift => shift.Type == "Arc"));
        Assert.All(result.MacroShiftDifferences.Where(shift => shift.Type == "Arc"),
            shift => Assert.Equal(["endAngle"], shift.Redrawn));
    }

    [Fact]
    public void AnglesStatedDifferentlyWithoutAMacroOffset_StayAnOrdinaryDifference()
    {
        PageExportComparison result = PageExportComparer.Compare(Merge(Page(12), ArcPage(4)),
            Merge(Page(12), ArcPage(4, endAngle: 1.5707963267948966)));

        Assert.Equal(4, result.Types["Arc"].Different.Count);
        Assert.Empty(result.Types["Arc"].Missing);
        Assert.Empty(result.MacroShiftDifferences);
    }

    [Fact]
    public void ARadiusChangeAtTheMacroOffset_IsNotExcused()
    {
        PageExportComparison result = PageExportComparer.Compare(Merge(Page(12), ArcPage(4)),
            Merge(Page(12, Dx, Dy), ArcPage(4, Dx, Dy, radius: 3)));

        Assert.Equal(4, result.Types["Arc"].Missing.Count);
        Assert.Equal(4, result.Types["Arc"].Extra.Count);
        Assert.Equal(12, result.MacroShiftDifferences.Count);
    }

    private static string Merge(string first, string second)
    {
        JObject page = JObject.Parse(first);
        ((JArray)page["placements"]!).Merge(JObject.Parse(second)["placements"]!);
        return page.ToString(Newtonsoft.Json.Formatting.None);
    }
}
