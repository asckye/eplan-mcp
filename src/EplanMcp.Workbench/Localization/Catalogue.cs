namespace EplanMcp.Workbench.Localization;

/// <summary>
/// Every text the Workbench shows or reads out (workbench-ui LOC-01), as one table of (key, English, Chinese): a key
/// cannot exist in one language only. StringsTests checks the rest: no key twice, nothing blank, the same placeholders
/// in both languages. The vendored Loc reads it through TIA's <c>Strings</c> (TiaStubs\Strings.cs).
/// </summary>
internal static class Catalogue
{
    public static IReadOnlyList<(string Key, string En, string Zh)> Entries { get; } =
    [
        ("App.Name", "EplanMcp Workbench", "EplanMcp Workbench"),

        // Read by the vendored theme's scroll bars (ScrollCommandNameConverter, Primer.xaml).
        ("Access.Scroll", "Scroll", "滚动"),
        ("Access.ScrollUp", "Scroll up", "向上滚动"),
        ("Access.ScrollDown", "Scroll down", "向下滚动"),
        ("Access.ScrollLeft", "Scroll left", "向左滚动"),
        ("Access.ScrollRight", "Scroll right", "向右滚动"),
        ("Access.ScrollPosition", "Scroll position", "滚动位置"),
    ];

    public static IReadOnlyDictionary<string, string> English { get; } = Build(entry => entry.En);

    public static IReadOnlyDictionary<string, string> Chinese { get; } = Build(entry => entry.Zh);

    private static Dictionary<string, string> Build(Func<(string Key, string En, string Zh), string> pick)
    {
        var table = new Dictionary<string, string>(Entries.Count, StringComparer.Ordinal);
        foreach ((string Key, string En, string Zh) entry in Entries)
        {
            table[entry.Key] = pick(entry);
        }

        return table;
    }
}
