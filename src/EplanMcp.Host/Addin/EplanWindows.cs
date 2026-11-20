using System.Runtime.InteropServices;

namespace EplanMcp.Host.Addin;

/// <summary>A child control of an EPLAN window: its dialog control ID and its window class.</summary>
internal sealed record EplanControl(int Id, string ClassName);

/// <summary>
/// A visible top-level window of an EPLAN process as Windows reports it: class, title (localized, so never used to
/// recognise a window), whether it is enabled, the window that owns it (0 for none) and its child controls.
/// </summary>
internal sealed record EplanWindow(nint Handle, string ClassName, string Title, bool Enabled, nint Owner, IReadOnlyList<EplanControl> Controls)
{
    /// <summary>How the window is named to the engineer: its title and class.</summary>
    public override string ToString() => $"'{Title}' ({ClassName})";
}

/// <summary>The Windows API behind <see cref="IStartedEplan.Windows"/> and <see cref="IStartedEplan.CloseWindow"/>. Never used by tests.</summary>
internal static class EplanWindows
{
    private const uint GwOwner = 4;
    private const uint WmClose = 0x0010;

    /// <summary>The visible top-level windows of <paramref name="processId"/>, with their child controls.</summary>
    public static IReadOnlyList<EplanWindow> Of(int processId)
    {
        var handles = new List<nint>();
        EnumWindows((window, _) =>
        {
            if (GetWindowThreadProcessId(window, out uint owner) != 0 && owner == (uint)processId && IsWindowVisible(window))
            {
                handles.Add(window);
            }

            return true;
        }, 0);
        return [.. handles.Select(h => new EplanWindow(h, ClassOf(h), TitleOf(h), IsWindowEnabled(h), GetWindow(h, GwOwner), ControlsOf(h)))];
    }

    /// <summary>Asks the window to close as its close button does (WM_CLOSE); a dialog answers it as its Cancel or Close button.</summary>
    public static void Close(EplanWindow window) => PostMessage(window.Handle, WmClose, 0, 0);

    private static List<EplanControl> ControlsOf(nint window)
    {
        var controls = new List<EplanControl>();
        EnumChildWindows(window, (child, _) =>
        {
            controls.Add(new(GetDlgCtrlID(child), ClassOf(child)));
            return true;
        }, 0);
        return controls;
    }

    private static string ClassOf(nint window)
    {
        char[] name = new char[256];
        int length = GetClassName(window, name, name.Length);
        return length > 0 ? new string(name, 0, length) : string.Empty;
    }

    private static string TitleOf(nint window)
    {
        char[] title = new char[512];
        int length = GetWindowText(window, title, title.Length);
        return length > 0 ? new string(title, 0, length) : string.Empty;
    }

    private delegate bool EnumProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(nint window);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] name, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] title, int capacity);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
