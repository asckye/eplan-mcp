using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.StructureDeleteExecute"/>: checks the plan's fingerprint again, backs the project up
/// (<see cref="ProjectBackups"/>), removes the unused identifiers in one transaction and reads back; one still there rolls all back.
/// </summary>
public sealed class StructureDeleteExecuteOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers, IPageReader pages, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<StructureDeleteArgs, StructureDeleteData>, IWriteOperation
{
    public const string Tool = "eplan_structure_delete";

    public override string Name => OperationNames.StructureDeleteExecute;

    protected override StructureDeleteData Execute(StructureDeleteArgs args)
    {
        StructureDeleteRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one structure.delete.plan returned."); }
            request = StructureDeleteRequest.Check(args, catalog, identifiers);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's structure identifiers changed after the plan was made, so nothing was deleted. Call eplan_structure_delete again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new StructureDeleteData { Project = request.Project.Name };
        if (request.Removing.Count == 0)
        { return data; }
        string path = request.Project.Path;
        data.Backup = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<bool> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            foreach (StructureIdentifier identifier in request.Removing)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                identifiers.Remove(path, identifier.Name);
            }
            var left = new HashSet<string>(identifiers.Read(path).Select(i => i.Name), StringComparer.Ordinal);
            string? kept = request.Removing.Select(i => i.Name).FirstOrDefault(left.Contains);
            if (kept is not null)
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN kept a structure identifier, so nothing was deleted. metadata.identifier names it.",
                    retryable: false, new Dictionary<string, string> { ["identifier"] = kept });
            }
            return true;
        });
        data.Identifiers = request.Removing;
        data.Warning = committed.Warning;
        return data;
    }
}
