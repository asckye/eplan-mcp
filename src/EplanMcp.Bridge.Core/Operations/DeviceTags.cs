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
}
