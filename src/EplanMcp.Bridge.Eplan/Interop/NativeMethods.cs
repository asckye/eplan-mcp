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
}
