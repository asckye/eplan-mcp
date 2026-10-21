using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Approvals;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Audit;

/// <summary>The audit record drawer; the window shows it over a scrim and gives focus back when it closes (DLG-10).</summary>
public partial class AuditRecordDrawer : UserControl
{
    /// <summary>The longest value shown whole: a write's change list is long, and the record is what the engineer reads.</summary>
    private const int MaxValueLength = 8000;

    public AuditRecordDrawer() => InitializeComponent();

    /// <summary>The close button or Esc asked to close the drawer.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"Open folder" was chosen for the record shown.</summary>
    public event EventHandler? FolderRequested;

    /// <summary>Shows <paramref name="record"/>, each value made plain (CTL-12), then focuses the title (DLG-10).</summary>
    internal void Show(AuditRecord record)
    {
        Place.Text = Loc.Current.T("Audit.Place", record.File, record.Line);
        ShowFolder(opened: true, null);
        Fields.ItemsSource = record.Fields.Select(f => KeyValuePair.Create(ProjectText.Line(f.Key), ProjectText.Line(f.Value, MaxValueLength))).ToList();
        UpdateLayout();
        Keyboard.Focus(Heading);
    }

    /// <summary>Says in the drawer, where it was asked, that Windows could not open the folder (STS-05).</summary>
    internal void ShowFolder(bool opened, string? folder)
    {
        FolderProblem.Text = opened ? string.Empty : Loc.Current.T("Audit.FolderFailed", folder ?? string.Empty);
        FolderProblem.Visibility = opened ? Visibility.Collapsed : Visibility.Visible;
        if (!opened)
        {
            AccessibleStatus.Announce(FolderProblem);
        }
    }

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

    private void OnFolder(object sender, RoutedEventArgs e) => FolderRequested?.Invoke(this, EventArgs.Empty);
}
