using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CheckRunExecute"/>: checks the arguments and the plan's fingerprint again, then runs the
/// check in one transaction and reads the messages back inside it, with one message collection, copied at once
/// (docs/tools/eplan_check_run.md, section 5; api-notes A11, A16). The messages are counted and listed as
/// <see cref="CheckListOperation"/> does by default: without the ones marked as done.
/// </summary>
public sealed class CheckRunExecuteOperation(IProjectCatalog catalog, IPageReader reader, ICheckRunner runner, IProjectMessageReader messages, ITransactionScopeFactory transactions)
    : Operation<CheckRunArgs, CheckRunData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_check_run".</summary>
    public const string Tool = "eplan_check_run";

    /// <summary>The most messages the result lists; eplan_check_list pages through all (tool spec 3).</summary>
    public const int MaxMessages = 20;

    public override string Name => OperationNames.CheckRunExecute;

    protected override CheckRunData Execute(CheckRunArgs args)
    {
        CheckRunRequest request;
        IReadOnlyList<string> pageIds;
        int before;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one check.run.plan returned.");
            }

            request = CheckRunRequest.Check(args, catalog, reader);
            (pageIds, before) = request.ReadState(runner, messages);
            if (!string.Equals(CheckRunRequest.StateFingerprint(pageIds, before), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "Pages in the check's scope, or the project's check messages, changed after the plan was made, so nothing was checked. Call eplan_check_run again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        // One EPLAN call: a cancel during it takes effect when it returns, as the rollback before the commit.
        Committed<IReadOnlyList<ProjectMessageRecord>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            runner.Run(request.Project.Path, request.Pages, request.Scheme ?? string.Empty);
            return messages.ReadMessages(request.Project.Path);
        });
        List<CheckMessage> open = [.. committed.Result.Where(r => !r.Done).Select(CheckListOperation.ToMessage)];
        return new CheckRunData
        {
            Project = request.Project.Name,
            PageCount = pageIds.Count,
            Scheme = request.Scheme,
            Before = before,
            Messages = [.. open.Take(MaxMessages)],
            Total = open.Count,
            Errors = open.Count(m => m.Severity == CheckMessage.Error),
            Warnings = open.Count(m => m.Severity == CheckMessage.Warning),
            Hints = open.Count(m => m.Severity == CheckMessage.Hint),
            Warning = committed.Warning,
        };
    }
}
