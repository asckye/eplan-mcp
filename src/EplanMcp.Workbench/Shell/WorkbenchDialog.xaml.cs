using System.Windows;
using System.Windows.Automation;

namespace EplanMcp.Workbench.Shell;

/// <summary>A confirmation of a step that cannot be undone (DLG-09): the safe answer is the default (DLG-04).</summary>
public partial class WorkbenchDialog : Window
{
    /// <param name="title">The question, which is also the window title (DLG-06).</param>
    /// <param name="message">What happens, in words; no "Warning" (TXT-06).</param>
    /// <param name="safe">The safe button, with its access key (KEY-06).</param>
    /// <param name="action">The verb that answers the title (TXT-05), with its access key.</param>
    public WorkbenchDialog(string title, string message, string safe, string action)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        Message.Text = message;
        SafeButton.Content = safe;
        ActionButton.Content = action;
        AutomationProperties.SetName(SafeButton, Plain(safe));
        AutomationProperties.SetName(ActionButton, Plain(action));
        Loaded += (_, _) => SafeButton.Focus(); // DLG-02: focus on the safe button
    }

    /// <summary>Asks; true only for the action button, never for Esc, Enter or closing the window.</summary>
    public static bool Confirm(Window owner, string title, string message, string safe, string action) =>
        new WorkbenchDialog(title, message, safe, action) { Owner = owner }.ShowDialog() == true;

    /// <summary>A label without its access key: "取消(_C)" is read as "取消", "E_xit" as "Exit" (A11Y-01).</summary>
    internal static string Plain(string label)
    {
        string text = label.EndsWith(')') && label.LastIndexOf("(_", StringComparison.Ordinal) is int open and >= 0 ? label[..open] : label;
        return text.Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private void OnAction(object sender, RoutedEventArgs e) => DialogResult = true;
}
