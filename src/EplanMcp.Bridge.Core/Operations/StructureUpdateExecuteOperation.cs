using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.StructureUpdateExecute"/>: checks the plan's fingerprint again, creates and describes the
/// identifiers in one transaction and reads them back. A read-back that differs rolls the write back.
/// </summary>
public sealed class StructureUpdateExecuteOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers, ITextTranslator translator, ITransactionScopeFactory transactions)
    : Operation<StructureUpdateArgs, StructureUpdateData>, IWriteOperation
{
    public const string Tool = "eplan_structure_update";

    public override string Name => OperationNames.StructureUpdateExecute;

    protected override StructureUpdateData Execute(StructureUpdateArgs args)
    {
        StructureUpdateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one structure.update.plan returned."); }
            request = StructureUpdateRequest.Check(args, catalog, identifiers, translator);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's structure identifiers changed after the plan was made, so nothing was changed. Call eplan_structure_update again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new StructureUpdateData { Project = request.Project.Name };
        if (request.Changes.Count == 0 && request.Sorts.Count == 0)
        { return data; }
        string path = request.Project.Path;
        Committed<List<StructureIdentifier>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            foreach (StructureUpdateRequest.Change change in request.Changes)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if (change.Create)
                { identifiers.Create(path, change.Name); }
                if (change.After is not null)
                { identifiers.Describe(path, change.Name, change.After); }
            }
            foreach (StructureUpdateRequest.OrderChange sort in request.Sorts)
            { identifiers.Sort(path, sort.Names); }
            IReadOnlyList<StructureIdentifier> after = identifiers.Read(path);
            foreach (StructureUpdateRequest.OrderChange sort in request.Sorts)
            {
                IEnumerable<string> first = after.Select(i => i.Name).Where(n => StructureNames.Prefix(n) == sort.Prefix).Take(sort.Names.Count);
                if (!first.SequenceEqual(sort.Names, StringComparer.Ordinal))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not sort a block as asked, so nothing was committed. metadata.identifier names the block.",
                        retryable: false, new Dictionary<string, string> { ["identifier"] = sort.Prefix });
                }
            }
            Dictionary<string, StructureIdentifier> stored = after.ToDictionary(i => i.Name, StringComparer.Ordinal);
            var read = new List<StructureIdentifier>();
            foreach (StructureUpdateRequest.Change change in request.Changes)
            {
                if (!stored.TryGetValue(change.Name, out StructureIdentifier? actual) || (change.After is not null && !TextLanguages.SameValue(actual.Description, change.After)))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not store a structure identifier as asked, so nothing was committed. metadata.identifier names it.",
                        retryable: false, new Dictionary<string, string> { ["identifier"] = change.Name });
                }
                read.Add(actual);
            }
            return read;
        });
        data.Identifiers = committed.Result;
        data.Warning = committed.Warning;
        return data;
    }
}
