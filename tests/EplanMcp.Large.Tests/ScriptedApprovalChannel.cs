using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The engineer of the Destructive tools' Large tests (M4 design 7; tool spec eplan_page_delete, section 7): grants a
/// question only when the test registered its tool, project and change list with <see cref="Approve"/> before the call,
/// each registration once, and denies every other question, including any confirmation that would lift a write
/// suspension. It lives only in this test assembly and only <see cref="DestructiveHost"/> registers it: eplanmcp.exe has
/// no switch, setting or environment variable that enables it, and its only channel is EPLAN's dialog
/// (PolicyEngineTests checks both).
/// </summary>
internal sealed class ScriptedApprovalChannel : IApprovalChannel
{
    /// <summary>The channel's name in the audit.</summary>
    public const string ChannelName = "large_test_script";

    private readonly Lock _lock = new();
    private readonly List<(string Tool, string Project, string[] Changes)> _approvals = [];
    private readonly List<ApprovalQuestion> _questions = [];

    public string Name => ChannelName;

    public string Where => "in the Large test's script";

    /// <summary>Every question asked so far, oldest first.</summary>
    public IReadOnlyList<ApprovalQuestion> Questions
    {
        get
        {
            lock (_lock)
            {
                return [.. _questions];
            }
        }
    }

    /// <summary>Lets the next question of <paramref name="tool"/> about <paramref name="project"/> with exactly these change lines be granted.</summary>
    public void Approve(string tool, string project, IEnumerable<string> changes)
    {
        lock (_lock)
        {
            _approvals.Add((tool, project, [.. changes]));
        }
    }

    /// <summary>Drops the registrations no question used, so they cannot grant a later test's question.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _approvals.Clear();
        }
    }

    public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        ApprovalDecision decision = ApprovalDecision.Denied;
        lock (_lock)
        {
            _questions.Add(question);
            int registered = question.Release is not null
                ? -1
                : _approvals.FindIndex(a => a.Tool == question.Tool
                    && string.Equals(a.Project, question.Plan.ProjectPath, StringComparison.OrdinalIgnoreCase)
                    && a.Changes.SequenceEqual(question.Changes, StringComparer.Ordinal));
            if (registered >= 0)
            {
                _approvals.RemoveAt(registered);
                decision = ApprovalDecision.Granted;
            }
        }

        // Bound to exactly the question asked, as EPLAN's dialog answers (ApprovalService checks the binding).
        return Task.FromResult<ApprovalAnswer?>(new ApprovalAnswer(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, decision));
    }
}
