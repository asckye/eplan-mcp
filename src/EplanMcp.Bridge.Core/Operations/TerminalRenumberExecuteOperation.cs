using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TerminalRenumberExecute"/>: checks the arguments, the scheme, the scope and the plan's
/// fingerprint again, backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN number the terminals in one
/// transaction and reads back which terminals changed their name and which still contain '?'
/// (docs/tools/eplan_terminal_renumber.md, section 5; S21 saw the numbering roll back and undo in one step, api-notes A24).
/// EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class TerminalRenumberExecuteOperation(IProjectCatalog catalog, IPageReader pages, ITerminalRenumberer renumberer, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<TerminalRenumberArgs, TerminalRenumberData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_terminal_renumber".</summary>
    public const string Tool = "eplan_terminal_renumber";

    public override string Name => OperationNames.TerminalRenumberExecute;

    protected override TerminalRenumberData Execute(TerminalRenumberArgs args)
    {
        TerminalRenumberRequest request;
        List<PlacedFunction> scope;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one terminal.renumber.plan returned.");
            }

            request = TerminalRenumberRequest.Check(args, catalog, renumberer);
            scope = request.Scope(renumberer);
            if (!string.Equals(TerminalRenumberRequest.FingerprintOf(scope), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The terminals in scope changed after the plan was made, so nothing was numbered. Call eplan_terminal_renumber again for a new plan.",
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
            : $"EPLAN gave {numbered.Unmatched.ToString(CultureInfo.InvariantCulture)} terminals new identifiers, so their changes are not listed; check them with eplan_terminal_strip_get.";
        return new TerminalRenumberData
        {
            Project = request.Project.Name,
            Changed = [.. numbered.Changed.Take(TerminalRenumberRequest.MaxChanged)],
            ChangedCount = numbered.Changed.Count,
            StillUnnumbered = [.. numbered.StillUnnumbered.Take(TerminalRenumberRequest.MaxListed)],
            StillUnnumberedCount = numbered.StillUnnumbered.Count,
            Warning = committed.Warning is null ? unmatched : unmatched is null ? committed.Warning : committed.Warning + " " + unmatched,
        };
    }

    /// <summary>Numbers, then reads the scope back and compares it with <paramref name="before"/>, terminal by terminal.</summary>
    private Numbered Renumber(TerminalRenumberRequest request, List<PlacedFunction> before)
    {
        renumberer.Renumber(request.Project.Path, request.Strips, request.OnlyUnnumbered, request.Scheme, request.Start, request.Step);
        List<PlacedFunction> after = request.Scope(renumberer);
        (List<RenamedObject> changed, int unmatched) = TerminalRenumberRequest.Changes(before, after);
        List<string> still = [.. after.Select(t => t.Name).Where(TerminalRenumberRequest.Unnumbered).Distinct(StringComparer.Ordinal)];
        return new Numbered(changed, unmatched, still);
    }

    private sealed class Numbered(List<RenamedObject> changed, int unmatched, List<string> stillUnnumbered)
    {
        public List<RenamedObject> Changed { get; } = changed;

        public int Unmatched { get; } = unmatched;

        public List<string> StillUnnumbered { get; } = stillUnnumbered;
    }
}
