namespace EplanMcp.Host.Addin;

/// <summary>An EPLAN window that may be closed for the engineer: closing it loses nothing and answers no question.</summary>
/// <param name="Name">How results and notes name it.</param>
/// <param name="Evidence">The api-note that shows closing it has no consequence.</param>
/// <param name="Matches">Recognises the window among all windows of its process, never by its localized title.</param>
internal sealed record HarmlessWindow(string Name, string Evidence, Func<EplanWindow, IReadOnlyList<EplanWindow>, bool> Matches);

/// <summary>
/// The windows EPLAN's closer closes before it asks EPLAN to close (ADR-0063, #664). The list is in code and grows one
/// window at a time, each with evidence. Anything else, such as a save, overwrite or delete confirmation, a licence
/// selection or a compatibility prompt, is left to the engineer and reported. EplanMcp's approval windows belong to
/// another process and are never among an EPLAN process's windows.
/// </summary>
internal static class HarmlessWindows
{
    /// <summary>
    /// EPLAN's system messages window: a dialog (<c>#32770</c>) owned by EPLAN's main frame, with the messages grid
    /// (1011), the detail box (1076), "show all sessions" (1112), "file" (1012) and its close button (2). Recorded live
    /// on EPLAN 2025.0.3.23616 (api-notes A172). The licence selection dialog, also a <c>#32770</c>, is owned by the
    /// splash screen and has none of 1011, 1076 or 1112.
    /// </summary>
    public static HarmlessWindow SystemMessages { get; } = new("system messages", "api-notes A172", IsSystemMessages);

    public static IReadOnlyList<HarmlessWindow> All { get; } = [SystemMessages];

    /// <summary>The entry <paramref name="window"/> matches, or null when it is not on the list.</summary>
    public static HarmlessWindow? Find(EplanWindow window, IReadOnlyList<EplanWindow> all) => All.FirstOrDefault(h => h.Matches(window, all));

    /// <summary>True for EPLAN's main frame, which the closer asks to close itself.</summary>
    public static bool IsMainFrame(EplanWindow window) => window.Owner == 0 && window.ClassName.StartsWith("AfxMDIFrame", StringComparison.Ordinal);

    private static bool IsSystemMessages(EplanWindow window, IReadOnlyList<EplanWindow> all) =>
        window.ClassName == "#32770"
        && all.Any(w => w.Handle == window.Owner && IsMainFrame(w))
        && Has(window, 1011, "BCGPGridCtrl")
        && Has(window, 1076, "Edit")
        && Has(window, 1112, "Button")
        && Has(window, 1012, "Button")
        && Has(window, 2, "Button");

    // BCGP grid classes carry a suffix of addresses (BCGPGridCtrl:2980000:8:10003:10), so the class is matched up to it.
    private static bool Has(EplanWindow window, int id, string className) =>
        window.Controls.Any(c => c.Id == id && (c.ClassName == className || c.ClassName.StartsWith(className + ":", StringComparison.Ordinal)));
}
