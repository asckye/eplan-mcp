using System.IO;
using System.Windows;
using System.Windows.Media;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The two font tokens, <c>Ui.Font</c> and <c>Ui.FontMono</c> (workbench-ui TYP-01): Maple Mono CN from the
/// <c>Fonts\</c> folder next to the exe when it is there, never installed; else, and for any character it lacks, the
/// spec's system fallbacks. Set on the application's own resources, which win over the vendored Primer.xaml.
/// </summary>
internal static class WorkbenchFonts
{
    public const string Family = "Maple Mono CN";

    /// <summary>
    /// Microsoft YaHei UI is named on purpose: with only Segoe UI Variable Text and en-US as the language, WPF draws
    /// Chinese in Yu Gothic UI's Japanese forms (spike S18).
    /// </summary>
    public const string UiFallback = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";

    public const string MonoFallback = "Cascadia Mono, Consolas, Microsoft YaHei UI";

    /// <summary>Sets both tokens in <paramref name="resources"/>; true when Maple Mono CN was found in <paramref name="appDirectory"/>.</summary>
    public static bool Apply(ResourceDictionary resources, string appDirectory)
    {
        // A folder as base URI, with its trailing separator: WPF then maps every weight of the family in it (S18).
        var folder = new Uri(Path.Combine(appDirectory, "Fonts") + Path.DirectorySeparatorChar);
        bool bundled = Directory.Exists(folder.LocalPath)
            && Fonts.GetFontFamilies(folder).Any(f => f.FamilyNames.Values.Contains(Family, StringComparer.Ordinal));
        resources["Ui.Font"] = bundled ? new FontFamily(folder, $"./#{Family}, {UiFallback}") : new FontFamily(UiFallback);
        resources["Ui.FontMono"] = bundled ? new FontFamily(folder, $"./#{Family}, {MonoFallback}") : new FontFamily(MonoFallback);
        return bundled;
    }
}
