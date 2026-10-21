using System.Text.RegularExpressions;

namespace EplanMcp.Workbench.Tests.Themes;

/// <summary>
/// The TIA theme is vendored (ADR-0017 decision 2, THIRD-PARTY-NOTICES.md): every copied file names its source and
/// TIA's commit and keeps TIA's copyright line, and only the files whose header says what changed differ from TIA.
/// </summary>
[Trait("Size", "Small")]
public sealed partial class VendoredThemeTests
{
    private const string Commit = "c89b7c20";

    public static TheoryData<string> VendoredFiles => [.. SourceScan.Vendored.Select(f => f.Path)];

    [Fact]
    public void TheVendoredFolder_HoldsTheThemeTheLocalizationAndTheHelpersPrimerNeeds()
    {
        string[] expected =
        [
            "Tia/Controls/AccessibleStatus.cs", "Tia/Controls/BoolConverters.cs", "Tia/Controls/ContrastSelection.cs", "Tia/Controls/LogTailView.cs",
            "Tia/Controls/ScrollCommandNameConverter.cs", "Tia/Controls/Ux.cs", "Tia/Controls/WorkbenchLogView.cs", "Tia/Localization/Loc.cs",
            "Tia/Localization/TrExtension.cs", "Tia/Themes/Icons.xaml", "Tia/Themes/Palette.Dark.xaml", "Tia/Themes/Palette.HighContrast.xaml",
            "Tia/Themes/Palette.Light.xaml", "Tia/Themes/Primer.xaml", "Tia/Themes/ThemeManager.cs",
        ];

        Assert.Equal(expected, SourceScan.Vendored.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(VendoredFiles))]
    public void EveryVendoredFile_NamesItsSourceAndCommitAndKeepsTiasCopyright(string path)
    {
        string header = string.Join('\n', SourceScan.Single(path).Text.Split('\n').Take(4));
        string source = "src/Studio/Gui/" + path["Tia/".Length..];

        Assert.Contains($"Vendored from TIA Portal Openness MCP, {source} at commit {Commit}.", header, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 bulaofen0036-coder. MIT License, see THIRD-PARTY-NOTICES.md.", header, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFilesThatSaySo_AreChanged()
    {
        string[] changed = [.. SourceScan.Vendored.Where(f => !f.Text.Contains("Unchanged below this header.", StringComparison.Ordinal)).Select(f => f.Path).Order(StringComparer.Ordinal)];

        Assert.Equal(["Tia/Controls/ContrastSelection.cs", "Tia/Themes/ThemeManager.cs"], changed);
        Assert.All(changed, path => Assert.Contains("Changed below: ", SourceScan.Single(path).Text, StringComparison.Ordinal));
    }

    [Fact]
    public void FilesThatRepeatTiasCode_OutsideTheVendoredFolder_KeepTiasCopyright()
    {
        string[] repeating = ["TiaStubs/EnumIndexConverter.cs", "TiaStubs/FeatureTone.cs", "TiaStubs/LogLevel.cs"];

        Assert.All(repeating, path => Assert.Matches(CopyrightLine(), SourceScan.Single(path).Text));
    }

    [GeneratedRegex(@"Copyright \(c\) 2026 bulaofen0036-coder\. MIT License")]
    private static partial Regex CopyrightLine();
}
