using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The typed confirmation before a Destructive write is approved (ADR-0035): the action is enabled only while the typed
/// text equals the question's <see cref="QuestionItem.ConfirmValue"/>, and the dialog closes unanswered when the question
/// ends meanwhile (the countdown keeps running behind it).
/// </summary>
public partial class DestructiveConfirmDialog : Window
{
    private readonly QuestionItem _item;

    internal DestructiveConfirmDialog(QuestionItem item)
    {
        InitializeComponent();
        _item = item;
        Loc loc = Loc.Current;
        Title = Heading.Text = loc.T("Confirm.Title", item.Tool);
        Backup.Text = item.Backup;
        Changes.Text = item.Changes;
        TypeLabel.Text = item.ConfirmLabel;
        CancelButton.Content = loc["Confirm.Cancel"];
        ActionButton.Content = loc["Confirm.Action"];
        AutomationProperties.SetName(CancelButton, WorkbenchDialog.Plain(loc["Confirm.Cancel"]));
        AutomationProperties.SetName(ActionButton, WorkbenchDialog.Plain(loc["Confirm.Action"]));
        Loaded += (_, _) => Typed.Focus();
        item.PropertyChanged += OnItemChanged;
        Closed += (_, _) => item.PropertyChanged -= OnItemChanged;
    }

    /// <summary>Whether <paramref name="typed"/> confirms: the exact value, apart from spaces around it.</summary>
    internal static bool Matches(string typed, string value) => value.Length > 0 && string.Equals(typed.Trim(), value, StringComparison.Ordinal);

    /// <summary>Asks; true only for the action button with the value typed, never for Enter, Esc or closing the window.</summary>
    internal static bool Confirm(Window owner, QuestionItem item) => new DestructiveConfirmDialog(item) { Owner = owner }.ShowDialog() == true;

    private void OnTyped(object sender, TextChangedEventArgs e) => ActionButton.IsEnabled = _item.IsPending && Matches(Typed.Text, _item.ConfirmValue);

    private void OnAction(object sender, RoutedEventArgs e)
    {
        if (_item.IsPending && Matches(Typed.Text, _item.ConfirmValue))
        {
            DialogResult = true;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_item.IsPending && IsLoaded)
        {
            Close();
        }
    }
}
