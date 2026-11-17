using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using EplanMcp.Bridge.Core.Diagnostics;
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
/// <para>
/// <see cref="ApprovalModalGuard"/> ends a modal loop that outlives its window, and every dialog re-enables the windows it
/// found enabled, since a loop ended from outside skips WPF's own re-enabling; the log records how each dialog ended.
/// </para>
internal sealed class EplanApprovalDialog(IBridgeLog? log = null) : IApprovalDialog
{
    public string Ask(ApprovalAskArgs question, TimeSpan timeout)
    {
        MainThread.Assert();
        var countdown = new ApprovalCountdown(timeout);
        var open = Stopwatch.StartNew();
        var shown = new Stopwatch();
        string? decision = null;
        bool closing = false;
        TimeSpan? closeAt = null;
        ApprovalModalEnd ended = ApprovalModalEnd.Closed;
        IntPtr handle = IntPtr.Zero;

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
        string ownerClass = owner == IntPtr.Zero ? "none" : ClassName(owner);
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

        // A timer on this (main) thread's dispatcher; nothing is invoked from another thread. It runs from before the
        // dialog appears, so the deadline closes it even if it never does: EPLAN must never stay blocked.
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            // MX_Dryer23 2026-11-08: the window was destroyed without closing (disposed, no Closing event); Close() then
            // did nothing and ShowDialog's frame ran on, so EPLAN stayed blocked. A window that is gone, or a close that
            // did not end the dialog within the grace time, ends the modal loop here.
            bool alive = closing || handle == IntPtr.Zero || NativeMethods.IsWindow(handle);
            if (ApprovalModalGuard.Exit(alive, closeAt, open.Elapsed) is { } end)
            {
                ended = end;
                timer.Stop();
                Dispatcher.ExitAllFrames();
                return;
            }

            if (closing)
            {
                return;
            }

            approve.IsEnabled = reject.IsEnabled = countdown.AcceptsClicks(open.Elapsed, Shown());
            left.Text = question.CountdownFormat.Replace("{0}", countdown.Remaining(open.Elapsed));
            if (countdown.HasExpired(open.Elapsed))
            {
                closeAt ??= open.Elapsed;
                window.Close();
            }
        };
        window.SourceInitialized += (_, _) => handle = new WindowInteropHelper(window).Handle;
        window.ContentRendered += (_, _) =>
        {
            shown.Start();
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
            closeAt ??= open.Elapsed;
            decision ??= countdown.Decide(ApprovalChoice.Close, open.Elapsed, Shown());
        };
        approve.Click += (_, _) => Choose(ApprovalChoice.Approve);
        reject.Click += (_, _) => Choose(ApprovalChoice.Reject);

        IntPtr[] enabledBefore = EnabledThreadWindows();
        try
        {
            timer.Start();
            window.ShowDialog();
        }
        finally
        {
            timer.Stop();
            IReadOnlyList<IntPtr> disabled = ApprovalModalGuard.ToReenable(enabledBefore, NativeMethods.IsWindow, NativeMethods.IsWindowEnabled);
            foreach (IntPtr stillDisabled in disabled)
            {
                NativeMethods.EnableWindow(stillDisabled, true);
            }

            log?.Write(ended == ApprovalModalEnd.Closed && disabled.Count == 0 ? BridgeLogLevel.Information : BridgeLogLevel.Warning, string.Format(
                System.Globalization.CultureInfo.InvariantCulture, "Approval dialog {0} ended: {1}, {2}, {3:0.0} s open, {4} window(s) re-enabled, owner {5}",
                question.RequestId, decision ?? ApprovalDecisions.Cancelled, ended, open.Elapsed.TotalSeconds, disabled.Count, ownerClass));
        }

        return decision ?? ApprovalDecisions.Cancelled;

        TimeSpan? Shown() => shown.IsRunning ? shown.Elapsed : null;

        void Choose(ApprovalChoice choice)
        {
            if (!closing && countdown.Decide(choice, open.Elapsed, Shown()) is { } chosen)
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

    /// <summary>The visible, enabled top-level windows of this (main) thread: the ones ShowDialog disables.</summary>
    private static IntPtr[] EnabledThreadWindows()
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumThreadWindows(NativeMethods.GetCurrentThreadId(), (window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) && NativeMethods.IsWindowEnabled(window))
            {
                windows.Add(window);
            }

            return true;
        }, IntPtr.Zero);
        return [.. windows];
    }

    /// <summary>
    /// EPLAN's main frame (window class <c>AfxMDIFrame*</c>), unless it is minimized: it would hide the windows it owns,
    /// the dialog included. Not <c>Process.MainWindowHandle</c>: that may be a transient window, and destroying an owner
    /// destroys the dialog with it (2026-11-12: destroyed after 0.8 s with nobody clicking). No frame: no owner.
    /// </summary>
    private static IntPtr VisibleMainWindow()
    {
        IntPtr frame = IntPtr.Zero;
        NativeMethods.EnumThreadWindows(NativeMethods.GetCurrentThreadId(), (window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) && ClassName(window).StartsWith("AfxMDIFrame", StringComparison.Ordinal))
            {
                frame = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return frame != IntPtr.Zero && !NativeMethods.IsIconic(frame) ? frame : IntPtr.Zero;
    }

    private static string ClassName(IntPtr window)
    {
        char[] name = new char[256];
        int length = NativeMethods.GetClassName(window, name, name.Length);
        return length > 0 ? new string(name, 0, length) : string.Empty;
    }
}
