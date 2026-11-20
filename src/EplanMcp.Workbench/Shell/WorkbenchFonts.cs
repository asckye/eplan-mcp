using System.Windows;
using System.Windows.Media;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The two font tokens, <c>Ui.Font</c> and <c>Ui.FontMono</c>, as the 2026-11-20 design names them: Inter for the
/// interface and JetBrains Mono for identifiers, both inside the assembly (ADR-0034 decision 3 as revised that day).
/// Chinese falls back to Microsoft YaHei UI, the design's own fallback, which Windows has. Set on the application's
/// own resources, which win over the vendored Primer.xaml.
/// </summary>
internal static class WorkbenchFonts
{
    /// <summary>
    /// Microsoft YaHei UI is named on purpose: with only Latin fonts before it and en-US as the language, WPF draws
    /// Chinese in Yu Gothic UI's Japanese forms (spike S18).
    /// </summary>
    public const string Ui = "./Fonts/#Inter, Microsoft YaHei UI, Segoe UI Variable Text, Segoe UI";

    /// <summary>Identifiers, paths, logs, times and numbers.</summary>
    public const string Mono = "./Fonts/#JetBrains Mono, Cascadia Mono, Consolas, Microsoft YaHei UI";

    /// <summary>Where <see cref="Ui"/> and <see cref="Mono"/> find the bundled files.</summary>
    public static readonly Uri BaseUri = new("pack://application:,,,/EplanMcp.Workbench;component/");

    /// <summary>Sets both tokens in <paramref name="resources"/>.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        resources["Ui.Font"] = new FontFamily(BaseUri, Ui);
        resources["Ui.FontMono"] = new FontFamily(BaseUri, Mono);
    }
}
