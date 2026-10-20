using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// Answers <paramref name="decision"/> (null: cannot ask), or what <see cref="Answers"/> says for the question, for
/// exactly the question asked unless <see cref="Bind"/> changes the binding; <see cref="Wait"/> runs while "the engineer
/// thinks"; or fails with <see cref="Failure"/>. Keeps every question.
/// </summary>
internal sealed class FakeApprovalChannel(string name, ApprovalDecision? decision) : IApprovalChannel
{
    public List<ApprovalQuestion> Questions { get; } = [];

    public BridgeError? Failure { get; init; }

    public Func<ApprovalQuestion, ApprovalDecision?>? Answers { get; init; }

    public Func<ApprovalAnswer, ApprovalAnswer> Bind { get; init; } = a => a;

    public Action Wait { get; init; } = () => { };

    public string Name => name;

    public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        if (Failure is not null)
        {
            throw new BridgeCallException(Failure);
        }

        Wait();
        ApprovalDecision? answer = Answers is null ? decision : Answers(question);
        return Task.FromResult(answer is { } d ? Bind(new ApprovalAnswer(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, d)) : null);
    }
}
