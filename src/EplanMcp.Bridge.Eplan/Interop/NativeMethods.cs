using System.Runtime.InteropServices;

namespace EplanMcp.Bridge.Eplan.Interop;

internal static class NativeMethods
{
    /// <summary>False while a modal dialog owned by the window is open (spike S1).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr window);

    /// <summary>True while the window is minimized; the windows it owns are hidden then.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

    public delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumThreadWindows(uint threadId, EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr window, [Out] char[] className, int maxCount);
}
