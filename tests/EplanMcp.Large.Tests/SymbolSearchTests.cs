using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>A52–A55 library discovery on the shared scratch fixture; never imports libraries or creates objects.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class SymbolSearchTests(LiveEplan eplan)
{
    private static readonly string[] _directions = ["Up", "Down", "Left", "Right"];
    [Fact]
    public async Task SymbolSearch_ReadsLibrariesVariantsAndPins_WithoutChangingProjectFiles()
    {
        Dictionary<string, (long Size, DateTime Written)> before = NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: true);
        var variants = new List<int>();
        string? cursor = null;
        do
        {
            JsonElement data = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = eplan.ProjectPath, ["library"] = "SPECIAL", ["name_contains"] = "CO", ["kind"] = "routing", ["limit"] = 1, ["cursor"] = cursor });
            Assert.Equal(4, data.GetProperty("total").GetInt32());
            JsonElement symbol = Assert.Single(data.GetProperty("symbols").EnumerateArray());
            Assert.Equal(("SPECIAL", "CO", "Routing", "routing"), (symbol.GetProperty("library").GetString(), symbol.GetProperty("name").GetString(), symbol.GetProperty("type").GetString(), symbol.GetProperty("kind").GetString()));
            variants.Add(symbol.GetProperty("variant").GetInt32());
            Assert.NotEmpty(symbol.GetProperty("connectionPoints").EnumerateArray());
            Assert.All(symbol.GetProperty("connectionPoints").EnumerateArray(), point =>
            {
                Assert.Equal(2, point.GetProperty("location").GetArrayLength());
                Assert.All(point.GetProperty("location").EnumerateArray(), coordinate => Assert.True(double.IsFinite(coordinate.GetDouble())));
                Assert.Contains(point.GetProperty("direction").GetString(), _directions);
            });
            cursor = data.GetProperty("nextCursor").GetString();
            Assert.InRange(variants.Count, 1, 4);
        } while (cursor is not null);
        Assert.Equal([0, 1, 2, 3], variants);

        JsonElement coil = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = eplan.ProjectPath, ["library"] = "IEC_symbol", ["name_contains"] = "K", ["kind"] = "function" });
        Assert.Contains(coil.GetProperty("symbols").EnumerateArray(), symbol => symbol.GetProperty("name").GetString() == "K");
        JsonElement empty = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = eplan.ProjectPath, ["library"] = "missing-library" });
        Assert.Empty(empty.GetProperty("symbols").EnumerateArray());
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: true).OrderBy(p => p.Key, StringComparer.Ordinal));
    }
}
