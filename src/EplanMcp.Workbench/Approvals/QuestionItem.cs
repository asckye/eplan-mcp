using System.ComponentModel;
using System.Globalization;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.ViewModels;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// A question as the approval queue shows it (docs/design/2026-10-20-m5-operability.md#workbench-implementation): every label from the
/// catalogue in the current language, every text from the Host made plain first (APR-08), the countdown as of now.
/// <see cref="Refresh"/> re-reads all of it after the question, the language or the clock moved.
/// </summary>
internal sealed class QuestionItem(Question question, TimeProvider clock) : INotifyPropertyChanged, IAccessibleItem
{
    private readonly CleanList _changes = ProjectText.List(question.Ask.Changes);

    public event PropertyChangedEventHandler? PropertyChanged;

    public Question Question { get; } = question;

    public string RequestId => Question.RequestId;

    public bool IsPending => Question.End is null;

    public bool IsRelease => Ask.Kind == QuestionKind.SuspensionRelease;

    public bool IsDestructive => Ask.Tier == QuestionTier.Destructive;

    public string Title => Text(IsRelease ? "Question.ReleaseTitle" : "Question.Title");

    public string Kind => Text(IsRelease ? "Question.Release" : "Question.Approval");

    public string Tool => ProjectText.Line(Ask.Tool);

    /// <summary>The kind as the queue's code word, the same in both languages, as in the audit log.</summary>
    public string KindCode => IsRelease ? "SUSPENSION" : IsDestructive ? "DESTRUCTIVE" : "WRITE";

    /// <summary>When the question arrived, local time.</summary>
    public string ArrivedText => Question.Arrived.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>The Host's ID of the write and when it arrived, for the detail's header.</summary>
    public string IdLine => ProjectText.Line(RequestId, 64) + " · " + ArrivedText;

    /// <summary>Waiting, or how it ended, for the status tag.</summary>
    public string Status => IsPending ? Text("Question.Pending") : Result;

    public string Tier => Text(IsDestructive ? "Question.Destructive" : "Question.Write");

    public string ProjectName => ProjectText.Line(Ask.Project.Name);

    public string ProjectPath => ProjectText.Line(Ask.Project.Path);

    public string Eplan => Loc.Current.T("Question.EplanValue", ProjectText.Line(Ask.Eplan.Version), Ask.Eplan.Variant is { } variant ? ProjectText.Line(variant) : Text("Question.Unknown"), Ask.Eplan.ProcessId);

    /// <summary>The name the client gave itself, never trusted: the label says "unverified" (APR-07).</summary>
    public string Client => Ask.ClientName is { Length: > 0 } name ? ProjectText.Line(name) : Text("Question.NoClient");

    /// <summary>The change list as plain lines, then how many the limit left out (APR-08).</summary>
    public string Changes => string.Join(Environment.NewLine, _changes.MoreLines == 0 ? _changes.Lines : [.. _changes.Lines, Loc.Current.T("Question.MoreLines", _changes.MoreLines)]);

    /// <summary>For a Write, how to undo it; for a Destructive write, that only the backup restores it (ADR-0035); for a release, what to check first, never Ctrl+Z (APR-07).</summary>
    public string Note => IsRelease ? Text("Question.ReleaseNote") : IsDestructive ? Text("Confirm.Message") : Loc.Current.T("Question.Undo", Tool);

    /// <summary>Where a Destructive write backs the project up first (APR-09); empty for any other.</summary>
    public string Backup => !IsDestructive ? string.Empty
        : Ask.BackupFolder is { Length: > 0 } folder ? Loc.Current.T("Question.BackupTo", ProjectText.Line(folder)) : Text("Question.NoBackup");

    /// <summary>For a release, the suspended write: time, tool and scope (APR-07); empty for an approval.</summary>
    public string Suspension => Ask.Suspension is not { } record ? string.Empty : Loc.Current.T(
        "Question.SuspendedValue",
        record.TimeUnixMs is { } time ? DateTimeOffset.FromUnixTimeMilliseconds(time).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : Text("Question.Unknown"),
        ProjectText.Line(record.Tool),
        record.Project is { } project ? Loc.Current.T("Question.ScopeProject", ProjectText.Line(project)) : Loc.Current.T("Question.ScopeInstance", record.ProcessId ?? 0));

    /// <summary>A Destructive write names what runs and ends in an ellipsis: a typed confirmation follows (ADR-0035).</summary>
    public string ApproveLabel => IsRelease ? Text("Question.Continue") : IsDestructive ? Loc.Current.T("Question.ApproveDestructive", Tool) : Text("Question.Approve");

    public string RejectLabel => Text(IsRelease ? "Question.KeepSuspended" : IsDestructive ? "Question.RejectDefault" : "Question.Reject");

    /// <summary>What the two answers do, beside them: which one is the safe one, or for a Write that they are equal (ADR-0014).</summary>
    public string AnswerHint => Text(IsRelease ? "Question.ReleaseHint" : IsDestructive ? "Question.DestructiveHint" : "Question.WriteHint");

    /// <summary>
    /// What the engineer types to approve a Destructive write (ADR-0035): a structured field of the question, never text
    /// from the change list. The protocol has no page or device field, so it is the project's name, or the tool when the
    /// project has none.
    /// </summary>
    public string ConfirmValue => ProjectName.Length > 0 ? ProjectName : Tool;

    public TimeSpan Left => Countdown.Left(Question, clock.GetUtcNow());

    public string CountdownText => Loc.Current.T(IsRelease ? "Question.ReleaseCountdown" : "Question.Countdown", Countdown.Clock(Left));

    /// <summary>The share of the time left, for the bar (APR-10).</summary>
    public double Remaining => Countdown.Fraction(Question, clock.GetUtcNow()) * 100;

    public bool IsWarning => IsPending && Countdown.IsWarning(Left);

    /// <summary>The tone of the countdown's text and bar: the warning colour in the last 20 seconds (APR-10).</summary>
    public FeatureTone Tone => IsWarning ? FeatureTone.Warning : FeatureTone.Normal;

    /// <summary>How the question ended (APR-11); empty while it waits.</summary>
    public string Result => Question.End switch
    {
        null => string.Empty,
        QuestionEnd.Approved => Text(IsRelease ? "Result.Continued" : "Result.Approved"),
        QuestionEnd.Rejected => Text(IsRelease ? "Result.KeptSuspended" : "Result.Rejected"),
        QuestionEnd.Expired => Text("Result.Expired"),
        QuestionEnd.Cancelled => Text("Result.Cancelled"),
        QuestionEnd.Disconnected => Text("Result.Disconnected"),
        _ => Text("Result.Closed"),
    };

    /// <summary>What became of an approved write (APR-12); empty for anything else.</summary>
    public string Outcome
    {
        get
        {
            string? state = Question.Execution switch
            {
                QuestionExecution.None => null,
                QuestionExecution.Running => "Outcome.Running",
                QuestionExecution.Committed => "Outcome.Committed",
                QuestionExecution.RolledBack => "Outcome.RolledBack",
                QuestionExecution.NotStarted => "Outcome.NotStarted",
                QuestionExecution.Unknown => "Outcome.Unknown",
                _ => "Outcome.NotReceived",
            };
            return state is null ? string.Empty
                : Question.Reason is { Length: > 0 } reason ? Loc.Current.T("Outcome.Because", Text(state), ProjectText.Line(reason, 64)) : Text(state);
        }
    }

    /// <summary>The unknown outcome is a warning: writes are suspended until the engineer confirms (ADR-0010).</summary>
    public bool OutcomeIsWarning => Question.Execution == QuestionExecution.Unknown;

    /// <summary>The tone of the outcome's text: the warning colour, beside the warning icon, when it is unknown (APR-12).</summary>
    public FeatureTone OutcomeTone => OutcomeIsWarning ? FeatureTone.Warning : FeatureTone.Normal;

    /// <summary>The row's name for screen readers (CTL-05): what it is, where, and how much time is left or how it ended.</summary>
    public string AccessibleName => IsPending
        ? Loc.Current.T("Question.ItemName", Tool, Tier, ProjectName, Countdown.Clock(Left))
        : Loc.Current.T("Question.AnsweredName", Tool, ProjectName, Result);

    /// <summary>The queue row's tone word (Workbench 4.0): "!" for a Write that waits, "✕" for a Destructive write or a release; how it ended once answered.</summary>
    public string BadgeGlyph => !IsPending ? (Question.End == QuestionEnd.Approved ? "✓" : "✕") : IsDestructive || IsRelease ? "✕" : "!";

    /// <summary>What kind of question this is, as the queue row's first line names it.</summary>
    public string Badge => !IsPending ? Result : Text(IsRelease ? "Question.BadgeRelease" : IsDestructive ? "Question.BadgeDestructive" : "Question.BadgeWrite");

    /// <summary>The badge's tone (Workbench 4.0): a Destructive write, a release and a refusal in the error colour; an approval in the success colour; else the warning colour.</summary>
    public string BadgeTone => !IsPending ? (Question.End == QuestionEnd.Approved ? "Success" : "Error") : IsDestructive || IsRelease ? "Error" : "Warning";

    /// <summary>The time left as <c>m:ss</c>, beside the badge; empty once answered.</summary>
    public string Timer => IsPending ? Countdown.Clock(Left) : ArrivedText;

    /// <summary>The project and how many changes, under the tool in the queue row.</summary>
    public string Summary => Loc.Current.T("Question.Summary", ProjectName, _changes.Lines.Count + _changes.MoreLines);

    /// <summary>The detail's first line: who asks, the Host's request ID and the tool, as data (APR-07, APR-08).</summary>
    public string MetaLine => Client + " · " + ProjectText.Line(RequestId, 64) + " · " + Tool;

    /// <summary>The tag beside the title: the time left and what happens at the end, or how the question ended.</summary>
    public string StateText => !IsPending ? Result
        : Loc.Current.T(IsRelease ? "Question.StateRelease" : IsDestructive ? "Question.StateDestructive" : "Question.StateWrite", Countdown.Clock(Left));

    /// <summary>The change list with a line number and, for lines the Host writes as "+ " or "- ", the diff's sign.</summary>
    public IReadOnlyList<DiffLine> DiffLines => [.. _changes.Lines.Select((line, i) => DiffLine.Of(i + 1, line))];

    /// <summary>"+3 −2" for the diff's header; empty when no line is an addition or removal.</summary>
    public string DiffCounts
    {
        get
        {
            int added = _changes.Lines.Count(l => DiffLine.Of(0, l).IsAdded);
            int removed = _changes.Lines.Count(l => DiffLine.Of(0, l).IsRemoved);
            return added + removed == 0 ? string.Empty : "+" + added.ToString(CultureInfo.InvariantCulture) + " −" + removed.ToString(CultureInfo.InvariantCulture);
        }
    }
    private ApprovalAskParams Ask => Question.Ask;

    /// <summary>Every property may have changed: the question ended, the language switched or a second passed.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static string Text(string key) => Loc.Current[key];
}

/// <summary>One line of a question's change list as the Workbench 4.0 diff shows it: its number and its sign.</summary>
internal sealed record DiffLine(string Number, string Sign, bool IsAdded, bool IsRemoved)
{
    /// <summary>A line is an addition or removal only when the Host wrote "+ " or "- " first: "-K1" is a device, not a removal.</summary>
    public static DiffLine Of(int number, string line)
    {
        bool added = line.StartsWith("+ ", StringComparison.Ordinal);
        bool removed = line.StartsWith("- ", StringComparison.Ordinal);
        return new DiffLine(number.ToString(CultureInfo.InvariantCulture), added ? "+" : removed ? "−" : string.Empty, added, removed);
    }
}
