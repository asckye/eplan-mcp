namespace EplanMcp.Workbench.Shell;

/// <summary>What opened a page; it decides where keyboard focus goes (docs/design/2026-10-20-m5-operability.md#workbench-implementation).</summary>
public enum NavigationOrigin
{
    /// <summary>The window opened: focus goes to the page heading.</summary>
    Startup,

    /// <summary>A menu item: focus stays on it.</summary>
    Menu,

    /// <summary>A keyboard shortcut: focus goes to the page heading.</summary>
    Shortcut,

    /// <summary>The tray icon, a notification or a second start: focus goes to the page heading.</summary>
    Reminder,
}
