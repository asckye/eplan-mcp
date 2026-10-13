using System.Diagnostics;
using System.Windows.Threading;
using Eplan.EplApi.Base.Internal;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Bridge.Eplan.Interop;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// The only place that calls <see cref="Dispatcher.Invoke(DispatcherPriority, TimeSpan, Delegate)"/> (coding standards
/// 5a). Implements the four S1 rules (api-notes A9):
/// <list type="number">
/// <item>EPLAN's preset WPF dispatcher with a start timeout; an abandoned dispatch never runs later;</item>
/// <item>everything is caught inside the delegate, because an exception escaping on the main thread terminates EPLAN;</item>
/// <item>a <see cref="LockingStep"/> is the first EPLAN object created on the main thread;</item>
/// <item>refuse while EPLAN's main window is disabled (a modal dialog would run us inside its message loop), and
/// refuse re-entry while one of our operations is running.</item>
/// </list>
/// <see cref="_running"/> and <see cref="_mainWindow"/> are touched on the main thread only.
/// </summary>
internal sealed class EplanDispatcher : IMainThreadDispatcher
{
    private static readonly object _ran = new();

    private readonly Dispatcher _dispatcher;
    private IntPtr _mainWindow;
    private bool _running;

    private EplanDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Must run on EPLAN's main thread (OnInitGui). Reports which source supplied the dispatcher.</summary>
    public static EplanDispatcher CaptureOnMainThread(out string source)
    {
        Dispatcher? preset = EplanMainThreadDispatcher.GetMainThreadDispatcher();
        source = preset is null ? "Dispatcher.CurrentDispatcher (EPLAN had none preset)" : "EplanMainThreadDispatcher (preset by EPLAN)";
        return new EplanDispatcher(preset ?? Dispatcher.CurrentDispatcher);
    }

    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
    {
        if (_dispatcher.CheckAccess())
        {
            // Invoke would run inline and bypass the timeout; callers are pipe threads only.
            return DispatchResult.Faulted(new InvalidOperationException("Run was called on EPLAN's main thread."));
        }

        DispatchResult? result = null;
        try
        {
            object? marker = _dispatcher.Invoke(DispatcherPriority.Normal, startTimeout, new Func<object>(() =>
            {
                result = RunOnMainThread(work);
                return _ran;
            }));
            return marker is null ? DispatchResult.Busy(BusyReason.MainThreadBusy) : result!;
        }
        catch (Exception ex)
        {
            // The dispatcher itself failed (for example EPLAN is shutting down); nothing ran.
            return DispatchResult.Faulted(ex);
        }
    }

    private DispatchResult RunOnMainThread(Func<object?> work)
    {
        try
        {
            if (_running)
            {
                return DispatchResult.Busy(BusyReason.Reentrant);
            }

            if (MainWindowDisabled())
            {
                return DispatchResult.Busy(BusyReason.ModalDialog);
            }

            _running = true;
            try
            {
                using (new LockingStep())
                {
                    return DispatchResult.Completed(work());
                }
            }
            finally
            {
                _running = false;
            }
        }
        catch (Exception ex)
        {
            return DispatchResult.Faulted(ex);
        }
    }

    private bool MainWindowDisabled()
    {
        if (_mainWindow == IntPtr.Zero || !NativeMethods.IsWindow(_mainWindow))
        {
            using var process = Process.GetCurrentProcess();
            _mainWindow = process.MainWindowHandle;
        }

        return _mainWindow != IntPtr.Zero && !NativeMethods.IsWindowEnabled(_mainWindow);
    }
}
