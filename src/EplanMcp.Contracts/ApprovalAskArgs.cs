namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.ApprovalAsk"/>: the approval dialog's texts, worded by the Host in the
/// engineer's user interface language (M3 design 4.6), the question's binding, which the answer repeats, and its
/// deadline.
/// </summary>
public sealed class ApprovalAskArgs
{
    /// <summary>The Host's request ID of the write.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>The fingerprint of the plan the dialog shows.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The SHA-256 of the write's arguments.</summary>
    public string ArgumentsHash { get; set; } = string.Empty;

    /// <summary>When the question expires, in Unix milliseconds (UTC); the dialog closes itself then.</summary>
    public long DeadlineUnixMs { get; set; }

    /// <summary>The window title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Above the change list: the tool, the project, and that these changes will be made.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The change list, one line per change, long values already shortened. Project data, shown as it is.</summary>
    public List<string> Changes { get; set; } = [];

    /// <summary>Below the change list: how to undo the step in EPLAN.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>The label of the button that approves.</summary>
    public string ApproveLabel { get; set; } = string.Empty;

    /// <summary>The label of the button that rejects.</summary>
    public string RejectLabel { get; set; } = string.Empty;

    /// <summary>The countdown line; <c>{0}</c> stands for the time left (m:ss).</summary>
    public string CountdownFormat { get; set; } = string.Empty;
}
