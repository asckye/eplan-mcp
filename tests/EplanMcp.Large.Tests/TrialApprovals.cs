using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>The engineer of these trials: grants each question of one tool bound to exactly what was asked, and keeps every question.</summary>
internal sealed class TrialApprovals(string tool) : IApprovalChannel
{
    private readonly List<ApprovalQuestion> _questions = [];

    public string Name => ScriptedApprovalChannel.ChannelName;

    public string Where => "in the Large test's script";

    public IReadOnlyList<ApprovalQuestion> Questions
    {
        get
        {
            lock (_questions)
            {
                return [.. _questions];
            }
        }
    }

    /// <summary>Forgets the questions asked so far, so a later assertion sees only its own call.</summary>
    public void Clear()
    {
        lock (_questions)
        {
            _questions.Clear();
        }
    }

    public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        lock (_questions)
        {
            _questions.Add(question);
        }

        ApprovalDecision decision = question.Tool == tool && question.Release is null ? ApprovalDecision.Granted : ApprovalDecision.Denied;
        return Task.FromResult<ApprovalAnswer?>(new ApprovalAnswer(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, decision));
    }
}
