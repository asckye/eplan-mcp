namespace EplanMcp.Workbench.Shell;

/// <summary>How a new question gets noticed without taking focus (docs/design/2026-10-20-m5-operability.md#workbench-implementation).</summary>
[Flags]
public enum Reminder
{
    None = 0,

    /// <summary>The taskbar button flashes until the engineer brings the window to the front.</summary>
    Flash = 1,

    /// <summary>A tray notification with the count and the tool, never project text.</summary>
    Notify = 2,
}
