using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TiaOpenness.Gui.Controls;

namespace EplanMcp.Workbench.Shell;

/// <summary>The tone of a toast: the colour of its dot.</summary>
internal enum ToastTone
{
    Success,
    Warning,
    Error,
    Info,
}

/// <summary>
/// Short messages at the bottom centre of the window (the Workbench 4.0 design): what just happened, in words, with a dot in
/// its tone. A toast takes no input and goes away by itself after <see cref="Lifetime"/>; screen readers hear it through a
/// polite live region. Anything that needs an answer or must stay (a question, an error to act on) is not a toast.
/// </summary>
internal static class Toasts
{
    /// <summary>The most toasts on screen at once; an older one leaves when a newer one would exceed it.</summary>
    public const int MaxShown = 4;

    private static readonly Duration _fade = new(TimeSpan.FromMilliseconds(180));

    /// <summary>A toast was asked for; the main window shows it.</summary>
    public static event Action<string, ToastTone>? Wanted;

    /// <summary>How long a toast stays; a test shortens it.</summary>
    internal static TimeSpan Lifetime { get; set; } = TimeSpan.FromMilliseconds(2600);

    /// <summary>How long an error stays: long enough to read a reason; the engineer may be looking elsewhere.</summary>
    internal static TimeSpan ErrorLifetime { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Asks for a toast; nothing happens while no window shows them.</summary>
    public static void Show(string text, ToastTone tone) => Wanted?.Invoke(text, tone);

    /// <summary>Adds a toast to <paramref name="layer"/>, says it through <paramref name="announcer"/>, and takes it away later.</summary>
    public static Border Add(Panel layer, TextBlock announcer, string text, ToastTone tone)
    {
        var dot = new Ellipse { Style = (Style)layer.FindResource("Workbench.StatusDot") };
        dot.SetResourceReference(Shape.FillProperty, tone switch
        {
            ToastTone.Success => "State.Success.Text",
            ToastTone.Warning => "State.Warning.Text",
            ToastTone.Error => "State.Error.Text",
            _ => "Accent.Text",
        });
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(FrameworkElement.StyleProperty, "Workbench.ToastText");
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(label);
        var toast = new Border { Child = row };
        toast.SetResourceReference(FrameworkElement.StyleProperty, "Workbench.Toast");

        while (layer.Children.Count >= MaxShown)
        {
            layer.Children.RemoveAt(0);
        }

        layer.Children.Add(toast);
        announcer.Text = text;
        announcer.Visibility = Visibility.Visible;
        AccessibleStatus.Announce(announcer);

        var timer = new DispatcherTimer(DispatcherPriority.Background, layer.Dispatcher) { Interval = tone == ToastTone.Error ? ErrorLifetime : Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Leave(layer, toast, announcer);
        };
        timer.Start();
        return toast;
    }

    /// <summary>Fades the toast out while it drops 4 px, then removes it; at once when the system's animations are off.</summary>
    private static void Leave(Panel layer, Border toast, TextBlock announcer)
    {
        void Remove()
        {
            layer.Children.Remove(toast);
            if (layer.Children.Count == 0)
            {
                // Nothing left to say: an empty live region would be a nameless text for screen readers.
                announcer.Text = string.Empty;
                announcer.Visibility = Visibility.Collapsed;
            }
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            Remove();
            return;
        }

        var drop = new TranslateTransform();
        toast.RenderTransform = drop;
        var fade = new DoubleAnimation(1, 0, _fade);
        fade.Completed += (_, _) => Remove();
        drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 4, _fade));
        toast.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
