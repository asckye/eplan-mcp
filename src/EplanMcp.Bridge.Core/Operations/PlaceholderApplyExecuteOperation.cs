using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PlaceholderApplyExecute"/> (docs/tools/eplan_placeholder_apply.md, section 5): checks the
/// arguments and the plan's fingerprint again, then applies the value set in one transaction and reads the placeholder
/// back. The read-back checks the placeholder itself (same value sets, variables and values); EPLAN does not tell which
/// referenced property a variable sets, so the referenced objects are not compared one by one, and unchanged current
/// values are reported, not refused (S29 compared only the current values). Any difference rolls back.
/// </summary>
public sealed class PlaceholderApplyExecuteOperation(
    IProjectCatalog catalog, IPageReader pages, IPlaceholderReader reader, IPlaceholderWriter writer, ITransactionScopeFactory transactions)
    : Operation<PlaceholderApplyArgs, PlaceholderApplyData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_placeholder_apply".</summary>
    public const string Tool = "eplan_placeholder_apply";

    public override string Name => OperationNames.PlaceholderApplyExecute;

    protected override PlaceholderApplyData Execute(PlaceholderApplyArgs args)
    {
        PlaceholderApplyRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one placeholder.apply.plan returned.");
            }

            request = PlaceholderApplyRequest.Check(args, catalog, pages, reader);
            if (!string.Equals(request.Fingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The placeholder changed after the plan was made, so nothing was changed. Call eplan_placeholder_apply again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        PlaceholderRecord before = request.Target;
        Committed<PlaceholderRecord> committed = TransactionRunner.Run(transactions, Tool, () => Apply(request));
        PlaceholderRecord after = committed.Result;
        return new PlaceholderApplyData
        {
            Project = request.Project.Name,
            Page = request.Page,
            Placeholder = before.Name,
            ValueSet = request.ValueSet,
            Values = [.. request.Values().Select(v => PlaceholderListOperation.Value(v.Value, null, v.Variable))],
            CurrentValuesBefore = [.. before.CurrentValues.Select(c => PlaceholderListOperation.Value(c, null, null))],
            CurrentValuesAfter = [.. after.CurrentValues.Select(c => PlaceholderListOperation.Value(c, null, null))],
            CurrentValuesChanged = !before.CurrentValues.Select(c => c.Raw).SequenceEqual(after.CurrentValues.Select(c => c.Raw)),
            Warning = committed.Warning,
        };
    }

    /// <summary>Applies the value set and checks the placeholder read back; throws, and so rolls back, when it differs.</summary>
    private PlaceholderRecord Apply(PlaceholderApplyRequest request)
    {
        PlaceholderRecord before = request.Target;
        PlaceholderRecord after = writer.Apply(request.Project.Path, request.Page, before.Name, request.ValueSet);
        string? problem =
            after.Page != before.Page || after.Name != before.Name ? "it is no longer the same placeholder"
            : !after.ValueSets.SequenceEqual(before.ValueSets, StringComparer.Ordinal) ? "its value sets changed"
            : !after.Properties.VariableNames.SequenceEqual(before.Properties.VariableNames, StringComparer.Ordinal) ? "its variables changed"
            : !after.Values.Select(v => v.Value.Raw).SequenceEqual(before.Values.Select(v => v.Value.Raw), StringComparer.Ordinal) ? "its stored values changed"
            : null;
        return problem is null
            ? after
            : throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"After applying the value set, {problem}, so the change was rolled back and nothing was changed. Ask the engineer to check the placeholder in EPLAN.",
                retryable: false,
                new Dictionary<string, string> { ["problem"] = problem });
    }
}
