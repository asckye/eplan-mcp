using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EplanMcp.Workbench.Settings;

/// <summary>The settings drawer; the window shows it over a scrim and gives focus back when it closes (DLG-10).</summary>
public partial class SettingsDrawer : UserControl
{
    public SettingsDrawer() => InitializeComponent();

    /// <summary>The close button or Esc asked to close the drawer.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Focus goes to the drawer's title when it opens (DLG-10).</summary>
    public void FocusHeading() => Keyboard.Focus(Heading);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
