namespace EplanMcp.Bridge.Core.Threading;

/// <summary>Why EPLAN's main thread refused or never picked up a piece of work (spike S1).</summary>
public enum BusyReason
{
    None,

    /// <summary>The main thread did not pick the work up within the start timeout.</summary>
    MainThreadBusy,

    /// <summary>EPLAN's main window is disabled: a modal dialog is open and the work would run inside its message loop.</summary>
    ModalDialog,

    /// <summary>Another EplanMcp operation is still running on the main thread (we were re-entered from its message pump).</summary>
    Reentrant,
}
