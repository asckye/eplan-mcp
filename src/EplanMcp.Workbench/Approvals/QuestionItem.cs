using System.ComponentModel;
using System.Globalization;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.ViewModels;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// A question as the approval queue shows it (workbench-ui APR-06, APR-07, APR-09 to APR-12): every label from the
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

    public string Tier => Text(IsDestructive ? "Question.Destructive" : "Question.Write");

    public string ProjectName => ProjectText.Line(Ask.Project.Name);

    public string ProjectPath => ProjectText.Line(Ask.Project.Path);

    public string Eplan => Loc.Current.T("Question.EplanValue", ProjectText.Line(Ask.Eplan.Version), Ask.Eplan.Variant is { } variant ? ProjectText.Line(variant) : Text("Question.Unknown"), Ask.Eplan.ProcessId);

    /// <summary>The name the client gave itself, never trusted: the label says "unverified" (APR-07).</summary>
    public string Client => Ask.ClientName is { Length: > 0 } name ? ProjectText.Line(name) : Text("Question.NoClient");

    /// <summary>The change list as plain lines, then how many the limit left out (APR-08).</summary>
    public string Changes => string.Join(Environment.NewLine, _changes.MoreLines == 0 ? _changes.Lines : [.. _changes.Lines, Loc.Current.T("Question.MoreLines", _changes.MoreLines)]);

    /// <summary>For an approval, how to undo it; for a release, what to check first, never Ctrl+Z (APR-07).</summary>
    public string Note => IsRelease ? Text("Question.ReleaseNote") : Loc.Current.T("Question.Undo", Tool);

    /// <summary>Where a Destructive write backs the project up first (APR-09); empty for any other.</summary>
    public string Backup => !IsDestructive ? string.Empty
        : Ask.BackupFolder is { Length: > 0 } folder ? Loc.Current.T("Question.BackupTo", ProjectText.Line(folder)) : Text("Question.NoBackup");

    /// <summary>For a release, the suspended write: time, tool and scope (APR-07); empty for an approval.</summary>
    public string Suspension => Ask.Suspension is not { } record ? string.Empty : Loc.Current.T(
        "Question.SuspendedValue",
        record.TimeUnixMs is { } time ? DateTimeOffset.FromUnixTimeMilliseconds(time).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : Text("Question.Unknown"),
        ProjectText.Line(record.Tool),
        record.Project is { } project ? Loc.Current.T("Question.ScopeProject", ProjectText.Line(project)) : Loc.Current.T("Question.ScopeInstance", record.ProcessId ?? 0));

    public string ApproveLabel => Text(IsRelease ? "Question.Continue" : "Question.Approve");

    public string RejectLabel => Text(IsRelease ? "Question.KeepSuspended" : "Question.Reject");

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

    /// <summary>The row's name for screen readers (CTL-05): what it is, where, and how much time is left or how it ended.</summary>
    public string AccessibleName => IsPending
        ? Loc.Current.T("Question.ItemName", Tool, Tier, ProjectName, Countdown.Clock(Left))
        : Loc.Current.T("Question.AnsweredName", Tool, ProjectName, Result);

    private ApprovalAskParams Ask => Question.Ask;

    /// <summary>Every property may have changed: the question ended, the language switched or a second passed.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static string Text(string key) => Loc.Current[key];
}
