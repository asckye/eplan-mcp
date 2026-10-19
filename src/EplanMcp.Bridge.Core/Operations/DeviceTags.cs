namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What the tools call a device and its tags, in one place so counts, lists and lookups agree (api-notes A11). A
/// device is a main function with a name; its tag is <c>Function.Name</c>, the full tag with structure
/// (<c>=GAA+A1-FC1</c>). The tag shown on the page is <c>VisibleName</c>, which can be empty or carry '&gt;' markers
/// (<c>&gt;=&gt;+&gt;-CU1</c>), and the same shown tag (<c>-FC1</c>) can exist in several structures.
/// </summary>
public static class DeviceTags
{
    /// <summary>Auxiliary functions belong to a main function's device; unnamed main functions cannot be looked up by tag.</summary>
    public static bool IsDevice(bool isMainFunction, string? name) => isMainFunction && !string.IsNullOrEmpty(name);

    /// <summary>The shown tag without its '&gt;' markers, or null when nothing is left.</summary>
    public static string? Shown(string? visibleName) =>
        visibleName?.Replace(">", string.Empty) is { Length: > 0 } shown ? shown : null;

    /// <summary>Whether the full tag or the shown tag contains <paramref name="text"/>, ignoring case.</summary>
    public static bool Contains(string name, string? visibleName, string text) =>
        name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
        || (Shown(visibleName)?.IndexOf(text, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

    /// <summary>
    /// The full tag of the one main function whose shown tag is <paramref name="tag"/>, both without '&gt;' markers and
    /// ignoring case; null when none matches. Several devices sharing the shown tag in different structures are
    /// AMBIGUOUS_NAME with their full tags as candidates, so the model can pick one (M2 design 4.2) and pass it again as
    /// <paramref name="argument"/>, the tool argument that held the tag.
    /// </summary>
    public static string? FindByShownTag(IEnumerable<FunctionName> mainFunctions, string tag, string argument = "tag")
    {
        string? wanted = Shown(tag);
        string[] matches =
        [
            .. mainFunctions
                .Where(f => f.Name.Length > 0 && wanted is not null && string.Equals(Shown(f.VisibleName), wanted, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .Distinct(StringComparer.Ordinal),
        ];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw ProjectResolver.Ambiguous(
                "device",
                tag,
                matches,
                $"Several devices in different structures show this tag; pass the full tag of one of the candidates as '{argument}'."),
        };
    }
}
