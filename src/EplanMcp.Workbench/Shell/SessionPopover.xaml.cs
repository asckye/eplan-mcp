using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Approvals;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The status bar's session popover (Workbench 4.0 design): whether the approval pipe takes questions, and the EPLAN,
/// project and client of the last question. Every text from a question is project or client data, made plain first.
/// Attaching to an EPLAN or choosing a process waits for ADR-0065.
/// </summary>
public partial class SessionPopover : UserControl
{
    public SessionPopover()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? ChecksRequested;

    /// <summary>Shows <paramref name="state"/> and <paramref name="last"/> (null before any question).</summary>
    internal void Show(ApprovalChannelState state, SessionFacts? last)
    {
        Loc loc = Loc.Current;
        (string text, string style) = state switch
        {
            ApprovalChannelState.Accepting => ("Session.Accepting", "Workbench.Tag.Success"),
            ApprovalChannelState.Full => ("Session.Full", "Workbench.Tag.Warning"),
            _ => ("Session.Taken", "Workbench.Tag.Error"),
        };
        StateText.Text = loc[text];
        StateTag.SetResourceReference(StyleProperty, style);

        bool known = last is not null;
        LastTile.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        NoneText.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
        if (last is { } facts)
        {
            EplanText.Text = facts.Eplan;
            ProcessText.Text = "PID " + facts.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DetailText.Text = loc.T("Session.Detail", facts.Project, facts.Client);
        }
    }

    internal void FocusHeading() => Keyboard.Focus(Heading);

    private void OnChecks(object sender, RoutedEventArgs e) => ChecksRequested?.Invoke(this, EventArgs.Empty);

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>The EPLAN, project and client of the last question, already made plain.</summary>
internal sealed record SessionFacts(string Eplan, int ProcessId, string Project, string Client);
