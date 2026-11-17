using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ConnectionNumberExecute"/>: checks the arguments, the scheme and the plan's fingerprint
/// again, backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN number the project's connections in one
/// transaction and reads back which wire numbers changed (docs/tools/eplan_connection_number.md, section 5; S21 saw the
/// numbering roll back and undo in one step, api-notes A24). EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class ConnectionNumberExecuteOperation(IProjectCatalog catalog, IPageReader pages, IConnectionNumberer numberer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<ConnectionNumberArgs, ConnectionNumberData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_connection_number".</summary>
    public const string Tool = "eplan_connection_number";

    public override string Name => OperationNames.ConnectionNumberExecute;

    protected override ConnectionNumberData Execute(ConnectionNumberArgs args)
    {
        ConnectionNumberRequest request;
        IReadOnlyList<DesignatedConnection> before;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one connection.number.plan returned.");
            }

            request = ConnectionNumberRequest.Check(args, catalog, numberer);
            before = numberer.ReadConnections(request.Project.Path);
            if (!string.Equals(ConnectionNumberRequest.FingerprintOf(before), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The project's connections or wire numbers changed after the plan was made, so nothing was numbered. Call eplan_connection_number again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<Numbered> committed = TransactionRunner.Run(transactions, Tool, () => Designate(request, before));
        Numbered numbered = committed.Result;
        string? unmatched = numbered.Unmatched == 0
            ? null
            : $"EPLAN gave {numbered.Unmatched.ToString(CultureInfo.InvariantCulture)} connections new identifiers, so their changes are not listed; ask the engineer to check those wire numbers in EPLAN.";
        return new ConnectionNumberData
        {
            Project = request.Project.Name,
            Changed = [.. numbered.Changed.Take(ConnectionNumberRequest.MaxChanged)],
            ChangedCount = numbered.Changed.Count,
            DesignatedAfter = numbered.DesignatedAfter,
            Warning = committed.Warning is null ? unmatched : unmatched is null ? committed.Warning : committed.Warning + " " + unmatched,
        };
    }

    /// <summary>Numbers, then reads every connection back and compares it with <paramref name="before"/>, connection by connection.</summary>
    private Numbered Designate(ConnectionNumberRequest request, IReadOnlyList<DesignatedConnection> before)
    {
        numberer.Designate(request.Project.Path, request.Scheme, request.Overwrite);
        IReadOnlyList<DesignatedConnection> after = numberer.ReadConnections(request.Project.Path);
        (List<NumberedConnection> changed, int unmatched) = ConnectionNumberRequest.Changes(before, after);
        return new Numbered(changed, unmatched, ConnectionNumberRequest.Designated(after));
    }

    private sealed class Numbered(List<NumberedConnection> changed, int unmatched, int designatedAfter)
    {
        public List<NumberedConnection> Changed { get; } = changed;

        public int Unmatched { get; } = unmatched;

        public int DesignatedAfter { get; } = designatedAfter;
    }
}
