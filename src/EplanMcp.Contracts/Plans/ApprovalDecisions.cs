namespace EplanMcp.Contracts;

/// <summary>The answers of the approval dialog (<see cref="ApprovalAskData.Decision"/>). Only <see cref="Granted"/> lets a write run.</summary>
public static class ApprovalDecisions
{
    /// <summary>The engineer approved.</summary>
    public const string Granted = "granted";

    /// <summary>The engineer rejected.</summary>
    public const string Denied = "denied";

    /// <summary>The dialog was closed without a choice.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The deadline passed first; the dialog closed itself.</summary>
    public const string Expired = "expired";
}
