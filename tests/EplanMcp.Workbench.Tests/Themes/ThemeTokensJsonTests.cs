using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EplanMcp.Workbench.Tests.Themes;

/// <summary>
/// ADR-0034 decision 6: the palettes' v2 colours come from the design system's tokens.json through tokens.map.json, and
/// eng/scripts/workbench-tokens.ps1 writes them. A design change that edits the tokens without running the script, or a
/// hand edit inside the generated block, fails here.
/// </summary>
[Trait("Size", "Small")]
public sealed partial class ThemeTokensJsonTests
{
    private static readonly XNamespace _x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("light", "Light")]
    [InlineData("dark", "Dark")]
    public void TheGeneratedBlock_MatchesTheTokens(string theme, string palette)
    {
        Dictionary<string, string> expected = Expected(theme);
        string text = SourceScan.Single($"Themes/Palette.{palette}.xaml").Text;
        Match block = Block().Match(text);
        Assert.True(block.Success, $"Palette.{palette}.xaml has no BEGIN/END v2 tokens block.");

        Dictionary<string, string> actual = XElement.Parse("<r xmlns:x=\"" + _x.NamespaceName + "\">" + block.Groups[1].Value + "</r>")
            .Elements().ToDictionary(e => (string)e.Attribute(_x + "Key")!, e => (string)e.Attribute("Color")!, StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryMappedKey_IsAVendoredColour_AndEveryTokenExists()
    {
        HashSet<string> vendored = [.. XDocument.Parse(SourceScan.Single("Tia/Themes/Palette.Light.xaml").Text).Root!.Elements()
            .Select(e => (string)e.Attribute(_x + "Key")!)];
        HashSet<string> tokens = [.. Tokens().Keys];
        using JsonDocument map = Map();

        foreach (JsonProperty section in map.RootElement.EnumerateObject().Where(s => !s.Name.StartsWith('_')))
        {
            foreach (JsonProperty entry in section.Value.EnumerateObject())
            {
                Assert.Contains(entry.Name, vendored);
                Assert.Contains(entry.Value.GetString()!, tokens);
            }
        }
    }

    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("#00000000", "#00000000")]
    [InlineData("#11223344", "#44112233")]
    [InlineData("rgba(0,0,0,0.25)", "#40000000")]
    [InlineData("rgba(30, 30, 30, 0.94)", "#F01E1E1E")]
    public void Colours_AreWrittenAsWpfReadsThem(string token, string wpf) => Assert.Equal(wpf, ToWpf(token));

    /// <summary>The script's rule: the map's <c>all</c> section, overridden per theme, each key to its token's value.</summary>
    private static Dictionary<string, string> Expected(string theme)
    {
        Dictionary<string, JsonElement> tokens = Tokens();
        using JsonDocument map = Map();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in map.RootElement.GetProperty("all").EnumerateObject())
        {
            keys[entry.Name] = entry.Value.GetString()!;
        }

        if (map.RootElement.TryGetProperty(theme, out JsonElement overrides))
        {
            foreach (JsonProperty entry in overrides.EnumerateObject())
            {
                keys[entry.Name] = entry.Value.GetString()!;
            }
        }

        return keys.ToDictionary(p => p.Key, p => ToWpf(tokens[p.Value].GetProperty(theme).GetString()!), StringComparer.Ordinal);
    }

    private static Dictionary<string, JsonElement> Tokens()
    {
        using JsonDocument document = JsonDocument.Parse(SourceScan.Single("Themes/tokens.json").Text);
        return document.RootElement.GetProperty("color").GetProperty("tokens").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("value").Clone(), StringComparer.Ordinal);
    }

    private static JsonDocument Map() => JsonDocument.Parse(SourceScan.Single("Themes/tokens.map.json").Text);

    private static string ToWpf(string value)
    {
        string v = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        Match rgba = Rgba().Match(v);
        if (rgba.Success)
        {
            int Channel(int group) => (int)double.Parse(rgba.Groups[group].Value, CultureInfo.InvariantCulture);
            int alpha = (int)Math.Round(double.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture) * 255, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"#{alpha:X2}{Channel(1):X2}{Channel(2):X2}{Channel(3):X2}");
        }

        string hex = v.TrimStart('#').ToUpperInvariant();
        return hex.Length == 8 ? "#" + hex[6..] + hex[..6] : "#" + hex;
    }

    [GeneratedRegex(@"<!-- BEGIN v2 tokens[^>]*-->\s*(.*?)\s*<!-- END v2 tokens -->", RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex(@"^rgba\(([\d.]+),([\d.]+),([\d.]+),([\d.]+)\)$")]
    private static partial Regex Rgba();
}
