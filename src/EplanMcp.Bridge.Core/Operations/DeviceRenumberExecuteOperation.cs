using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceRenumberExecute"/>: checks the arguments, the scope and the plan's fingerprint again,
/// backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN number the tags in one transaction and reads back
/// which devices changed and which still contain '?', which S12 found EPLAN may leave (docs/tools/eplan_device_renumber.md,
/// section 5; api-notes A18). EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class DeviceRenumberExecuteOperation(IProjectCatalog catalog, IPageReader pages, IDeviceRenumberer renumberer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<DeviceRenumberArgs, DeviceRenumberData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_device_renumber".</summary>
    public const string Tool = "eplan_device_renumber";

    public override string Name => OperationNames.DeviceRenumberExecute;

    protected override DeviceRenumberData Execute(DeviceRenumberArgs args)
    {
        DeviceRenumberRequest request;
        List<PlacedFunction> scope;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one device.renumber.plan returned.");
            }

            request = DeviceRenumberRequest.Check(args, catalog);
            scope = request.Scope(renumberer);
            if (!string.Equals(DeviceRenumberRequest.FingerprintOf(scope), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The functions in scope changed after the plan was made, so nothing was numbered. Call eplan_device_renumber again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<Numbered> committed = TransactionRunner.Run(transactions, Tool, () => Renumber(request, scope));
        Numbered numbered = committed.Result;
        string? unmatched = numbered.Unmatched == 0
            ? null
            : $"EPLAN gave {numbered.Unmatched.ToString(CultureInfo.InvariantCulture)} functions new identifiers, so the changes of their devices are not listed; check them with eplan_device_list.";
        return new DeviceRenumberData
        {
            Project = request.Project.Name,
            Changed = [.. numbered.Changed.Take(DeviceRenumberRequest.MaxChanged)],
            ChangedCount = numbered.Changed.Count,
            StillUnnumbered = [.. numbered.StillUnnumbered.Take(DeviceRenumberRequest.MaxListed)],
            StillUnnumberedCount = numbered.StillUnnumbered.Count,
            Warning = committed.Warning is null ? unmatched : unmatched is null ? committed.Warning : committed.Warning + " " + unmatched,
        };
    }

    /// <summary>Numbers, then reads the scope back and compares it with <paramref name="before"/>, function by function.</summary>
    private Numbered Renumber(DeviceRenumberRequest request, List<PlacedFunction> before)
    {
        renumberer.Renumber(request.Project.Path, request.Pages, request.OnlyUnnumbered, request.Scheme, request.Start, request.Step);
        List<PlacedFunction> after = request.Scope(renumberer);
        (List<RenamedObject> changed, int unmatched) = DeviceRenumberRequest.Changes(before, after);
        List<string> still = [.. after.Select(f => DeviceRenumberRequest.DeviceOf(f.Name)).Where(tag => tag.Length > 0 && DeviceRenumberRequest.Unnumbered(tag)).Distinct(StringComparer.Ordinal)];
        return new Numbered(changed, unmatched, still);
    }

    private sealed class Numbered(List<RenamedObject> changed, int unmatched, List<string> stillUnnumbered)
    {
        public List<RenamedObject> Changed { get; } = changed;

        public int Unmatched { get; } = unmatched;

        public List<string> StillUnnumbered { get; } = stillUnnumbered;
    }
}
