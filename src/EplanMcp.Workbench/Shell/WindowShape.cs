using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// What makes the borderless main window feel like a Windows 11 window: rounded corners from the system (it draws
/// them only when asked for a window without a standard frame), and a short entrance for the page that was switched
/// to. Both are skipped where the system does not offer them or the engineer turned animations off.
/// </summary>
internal static class WindowShape
{
    private const int CornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE, Windows 11 and later
    private const int Round = 2; // DWMWCP_ROUND
    private static readonly Duration _enter = new(TimeSpan.FromMilliseconds(160));

    /// <summary>Asks the system to round the window's corners; older Windows ignores the attribute.</summary>
    public static void RoundCorners(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle != 0)
        {
            int value = Round;
            _ = DwmSetWindowAttribute(handle, CornerPreference, ref value, sizeof(int));
        }
    }

    /// <summary>Fades the page in while it rises a few pixels; nothing when the system's animations are off.</summary>
    public static void Enter(FrameworkElement page)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var rise = new TranslateTransform();
        page.RenderTransform = rise;
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, _enter) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, _enter) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
