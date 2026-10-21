using EplanMcp.Workbench.Localization;

namespace TiaOpenness.Gui.Localization;

/// <summary>
/// The tables TIA's vendored <see cref="Loc"/> reads. In TIA they hold TIA's own texts; here they are EplanMcp's
/// <see cref="Catalogue"/>, so that Loc, the Tr markup extension and the texts inside Primer.xaml read one table.
/// </summary>
internal static class Strings
{
    public static IReadOnlyDictionary<string, string> English => Catalogue.English;

    public static IReadOnlyDictionary<string, string> Chinese => Catalogue.Chinese;
}
