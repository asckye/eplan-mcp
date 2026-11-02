using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal static class TNodeFixture
{
    // A75: PageReviewTests "cable" native helper read, page =GAA+A1&EFS1/103, node 423458.
    internal static ConnectionCreatePlanner.Scene Scene() => new()
    {
        Page = new()
        {
            Page = "=GAA+A1&EFS1/103",
            GridSize = 4,
            PageSize = [420, 297],
            Placements =
            [
                new()
                {
                    Kind = "SymbolReference", Locator = new() { Type = 13, Oid = 423458 }, Location = [120, 160],
                    // T3b gives the size: centre a 3 x 4.5 mm outline on the 120..122 x 160 pin envelope.
                    Bounds = [119.5, 157.75, 122.5, 162.25]
                },
                new() { Kind = "Function", Locator = new() { Type = 17, Oid = 69842 }, Location = [120, 220] },
                new() { Kind = "Function", Locator = new() { Type = 17, Oid = 69843 }, Location = [120, 100] },
                new() { Kind = "Function", Locator = new() { Type = 17, Oid = 69844 }, Location = [200, 160] }
            ]
        },
        Pins = new()
        {
            ["13/423458/0"] = new() { Location = [120, 160], Direction = "Down" },
            ["13/423458/1"] = new() { Location = [120, 160], Direction = "Up" },
            ["13/423458/2"] = new() { Location = [122, 160], Direction = "Right" },
            ["17/69842/0"] = new() { Location = [120, 220], Direction = "Down" },
            ["17/69843/0"] = new() { Location = [120, 100], Direction = "Up" },
            ["17/69844/0"] = new() { Location = [200, 160], Direction = "Left" }
        },
        Graphical = [[[120, 160], [120, 100]], [[120, 160], [120, 220]], [[122, 160], [200, 160]]]
    };
}
