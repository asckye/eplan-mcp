using System.Windows;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// The one way the Workbench's switches change whether ordinary writes ask (ADR-0061): turning approval off first asks
/// once and says what still asks; turning it back on never asks.
/// </summary>
internal static class ApprovalPolicyUi
{
    /// <summary>Asks before approval is turned off; a test replaces it.</summary>
    internal static Func<DependencyObject, bool> ConfirmOff { get; set; } = Ask;

    /// <summary>Null when the engineer kept approval on; else whether the file took the new value.</summary>
    public static bool? Set(SafetyPolicyStore policy, bool require, DependencyObject from)
    {
        if (!require && !ConfirmOff(from))
        {
            return null;
        }

        bool saved = policy.SetRequireNormalWriteApproval(require);
        if (saved)
        {
            Toasts.Show(Loc.Current[require ? "Toast.ApprovalOn" : "Toast.ApprovalOff"], require ? ToastTone.Success : ToastTone.Warning);
        }

        return saved;
    }

    private static bool Ask(DependencyObject from)
    {
        Loc loc = Loc.Current;
        return WorkbenchDialog.Confirm(Window.GetWindow(from)!, loc["Policy.OffTitle"], loc["Policy.OffMessage"], loc["Policy.OffCancel"], loc["Policy.OffConfirm"]);
    }
}
