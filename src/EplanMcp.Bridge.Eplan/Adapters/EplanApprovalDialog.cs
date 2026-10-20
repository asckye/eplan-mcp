using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Interop;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The approval dialog, the only window the Bridge shows (coding standards 9.4): a WPF window owned by EPLAN's main
/// window and shown modally on the main thread, so EPLAN's windows are disabled meanwhile and other Bridge calls get
/// EPLAN_BUSY (api-notes A9, M3 design 4.7). Following the TIA project's approval card: Reject and Approve have the same
/// size, neither is the default button and neither has an access key, the focus starts on the change list, and the
/// countdown is visible; the timing rules are <see cref="ApprovalCountdown"/>'s. No EPLAN API is used.
/// </summary>
internal sealed class EplanApprovalDialog : IApprovalDialog
{
    public string Ask(ApprovalAskArgs question, TimeSpan timeout)
    {
        MainThread.Assert();
        var countdown = new ApprovalCountdown(timeout);
        var shown = new Stopwatch();
        string? decision = null;
        bool closing = false;

        Button reject = DecisionButton(question.RejectLabel);
        Button approve = DecisionButton(question.ApproveLabel);
        var buttons = new UniformGrid { Rows = 1, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(reject);
        buttons.Children.Add(approve);
        TextBlock left = Text(question.CountdownFormat.Replace("{0}", countdown.Remaining(TimeSpan.Zero)));
        var footer = new StackPanel();
        footer.Children.Add(Text(question.Note));
        footer.Children.Add(left);
        footer.Children.Add(buttons);
        TextBlock summary = Text(question.Summary);
        var changes = new TextBox
        {
            Text = string.Join(Environment.NewLine, question.Changes),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
            Margin = new Thickness(0, 8, 0, 8),
        };
        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(summary, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(summary);
        layout.Children.Add(footer);
        layout.Children.Add(changes);

        IntPtr owner = VisibleMainWindow();
        var window = new Window
        {
            Title = question.Title,
            Content = layout,
            Width = 640,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            ShowInTaskbar = owner == IntPtr.Zero,
            WindowStartupLocation = owner == IntPtr.Zero ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        new WindowInteropHelper(window).Owner = owner;

        // A timer on this (main) thread's dispatcher; nothing is invoked from another thread.
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (closing)
            {
                return;
            }

            approve.IsEnabled = reject.IsEnabled = countdown.AcceptsClicks(shown.Elapsed);
            left.Text = question.CountdownFormat.Replace("{0}", countdown.Remaining(shown.Elapsed));
            if (countdown.HasExpired(shown.Elapsed))
            {
                window.Close();
            }
        };
        window.ContentRendered += (_, _) =>
        {
            shown.Start();
            timer.Start();
            changes.Focus();
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !closing)
            {
                e.Handled = true;
                window.Close();
            }
        };
        window.Closing += (_, _) =>
        {
            closing = true;
            timer.Stop();
            decision ??= countdown.Decide(ApprovalChoice.Close, shown.Elapsed);
        };
        approve.Click += (_, _) => Choose(ApprovalChoice.Approve);
        reject.Click += (_, _) => Choose(ApprovalChoice.Reject);

        try
        {
            window.ShowDialog();
        }
        finally
        {
            timer.Stop();
        }

        return decision ?? ApprovalDecisions.Cancelled;

        void Choose(ApprovalChoice choice)
        {
            if (!closing && countdown.Decide(choice, shown.Elapsed) is { } chosen)
            {
                decision = chosen;
                window.Close();
            }
        }
    }

    /// <summary>Disabled until the click guard has passed; the label is plain text, so it has no access key.</summary>
    private static Button DecisionButton(string label)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label },
            IsEnabled = false,
            MinWidth = 100,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 8, 0, 0),
        };
        AutomationProperties.SetName(button, label);
        return button;
    }

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };

    /// <summary>EPLAN's main window, unless it is minimized: it would hide the windows it owns, the dialog included.</summary>
    private static IntPtr VisibleMainWindow()
    {
        using var process = Process.GetCurrentProcess();
        IntPtr main = process.MainWindowHandle;
        return main != IntPtr.Zero && !NativeMethods.IsIconic(main) ? main : IntPtr.Zero;
    }
}
