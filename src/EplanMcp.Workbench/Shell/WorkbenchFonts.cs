using System.Windows;
using System.Windows.Media;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The two font tokens, <c>Ui.Font</c> and <c>Ui.FontMono</c>: Windows' own fonts, no font shipped with the Workbench
/// (ADR-0034 decision 3). Set on the application's own resources, which win over the vendored Primer.xaml.
/// </summary>
internal static class WorkbenchFonts
{
    /// <summary>
    /// Microsoft YaHei UI is named on purpose: with only Segoe UI Variable Text and en-US as the language, WPF draws
    /// Chinese in Yu Gothic UI's Japanese forms (spike S18).
    /// </summary>
    public const string Ui = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";

    /// <summary>Identifiers, paths, logs, times and numbers.</summary>
    public const string Mono = "Cascadia Mono, Consolas, Microsoft YaHei UI";

    /// <summary>Sets both tokens in <paramref name="resources"/>.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        resources["Ui.Font"] = new FontFamily(Ui);
        resources["Ui.FontMono"] = new FontFamily(Mono);
    }
}
