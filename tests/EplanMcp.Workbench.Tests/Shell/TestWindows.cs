using System.Windows;
using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>A real main window for one test, shown without taking the foreground, then closed; on the WPF thread.</summary>
internal static class TestWindows
{
    public static void With(Action<MainWindow> test)
    {
        var window = new MainWindow { ShowActivated = false };
        try
        {
            window.Show();
            WpfContext.Drain();
            test(window);
        }
        finally
        {
            window.Close();
        }
    }

    public static void Resize(Window window, double width)
    {
        window.Width = width;
        WpfContext.Drain();
    }

    public static void Click(UIElement element)
    {
        element.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        WpfContext.Drain();
    }
}
