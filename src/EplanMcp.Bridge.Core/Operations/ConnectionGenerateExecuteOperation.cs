using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ConnectionGenerateExecute"/>: checks the arguments and the plan's fingerprint again, then
/// generates connections on every page in one transaction and reads them back inside it, before the commit, to list
/// what was added and removed (docs/tools/eplan_connection_generate.md, section 5; api-notes A13, A14).
/// </summary>
public sealed class ConnectionGenerateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IConnectionGenerator generator, ITransactionScopeFactory transactions)
    : Operation<ConnectionGenerateArgs, ConnectionGenerateData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_connection_generate".</summary>
    public const string Tool = "eplan_connection_generate";

    public override string Name => OperationNames.ConnectionGenerateExecute;

    protected override ConnectionGenerateData Execute(ConnectionGenerateArgs args)
    {
        ConnectionGenerateRequest request;
        IReadOnlyList<IReadOnlyList<ConnectionRecord>> before;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one connection.generate.plan returned.");
            }

            request = ConnectionGenerateRequest.Check(args, catalog, reader);
            before = generator.ReadConnections(request.Project.Path, request.Pages);
            if (!string.Equals(request.StateFingerprint(before, generator.ObjectIds(request.Project.Path, request.Pages)), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "Objects or connections on these pages changed after the plan was made, so nothing was generated. Call eplan_connection_generate again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        // The connections before are the ones just read for the fingerprint, in this same main-thread call.
        Committed<IReadOnlyList<IReadOnlyList<ConnectionRecord>>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            generator.Generate(request.Project.Path, request.Pages);
            return generator.ReadConnections(request.Project.Path, request.Pages);
        },
        // Live T5b: a generation that changes nothing must not leave an undo step (the Host reports not_started).
        after => !BridgeJson.FromObject(after).ToString(Newtonsoft.Json.Formatting.None).Equals(BridgeJson.FromObject(before).ToString(Newtonsoft.Json.Formatting.None), StringComparison.Ordinal));
        return new ConnectionGenerateData
        {
            Project = request.Project.Name,
            Pages = ConnectionChanges.Compare(request.Pages, before, committed.Result),
            Warning = committed.Warning,
        };
    }
}
