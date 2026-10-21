using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CableRenumberExecute"/>: checks the arguments, the scheme, the scope and the plan's
/// fingerprint again, backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN number the cables in one
/// transaction and reads back which cables changed their tag (docs/tools/eplan_cable_renumber.md, section 5; S21 saw the
/// numbering roll back and undo in one step, api-notes A24). EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class CableRenumberExecuteOperation(IProjectCatalog catalog, IPageReader pages, IDeviceReader devices, ICableRenumberer renumberer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<CableRenumberArgs, CableRenumberData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_cable_renumber".</summary>
    public const string Tool = "eplan_cable_renumber";

    public override string Name => OperationNames.CableRenumberExecute;

    protected override CableRenumberData Execute(CableRenumberArgs args)
    {
        CableRenumberRequest request;
        List<PlacedFunction> scope;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one cable.renumber.plan returned.");
            }

            request = CableRenumberRequest.Check(args, catalog, renumberer);
            scope = request.Scope(renumberer, devices);
            if (!string.Equals(CableRenumberRequest.FingerprintOf(scope), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The cables in scope changed after the plan was made, so nothing was numbered. Call eplan_cable_renumber again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<(List<RenamedObject> Changed, int Unmatched)> committed = TransactionRunner.Run(transactions, Tool, () => Renumber(request, scope));
        (List<RenamedObject> changed, int unmatched) = committed.Result;
        string? lost = unmatched == 0
            ? null
            : $"EPLAN gave {unmatched.ToString(CultureInfo.InvariantCulture)} cable functions new identifiers, so their changes are not listed; check the cables with eplan_device_list and category Cable.";
        return new CableRenumberData
        {
            Project = request.Project.Name,
            Changed = [.. changed.Take(CableRenumberRequest.MaxChanged)],
            ChangedCount = changed.Count,
            Warning = committed.Warning is null ? lost : lost is null ? committed.Warning : committed.Warning + " " + lost,
        };
    }

    /// <summary>
    /// Numbers, then reads every cable back and compares it with <paramref name="before"/> by session identifier: the
    /// given cables are found again by identifier, since their tags may have changed.
    /// </summary>
    private (List<RenamedObject> Changed, int Unmatched) Renumber(CableRenumberRequest request, List<PlacedFunction> before)
    {
        renumberer.Renumber(request.Project.Path, request.Tags(before), request.Scheme, request.KeepExisting, request.Start, request.Step);
        return RenamedFunctions.Between(before, renumberer.ReadCables(request.Project.Path));
    }
}
