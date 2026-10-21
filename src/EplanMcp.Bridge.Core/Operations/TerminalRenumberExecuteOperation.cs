using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TerminalRenumberExecute"/>: checks the arguments, the scheme, the scope and the plan's
/// fingerprint again, backs the project up (<see cref="ProjectBackups"/>), then lets EPLAN number the terminals in one
/// transaction and reads back which terminals changed their name and which still contain '?'
/// (docs/tools/eplan_terminal_renumber.md, section 5; S21 saw the numbering roll back and undo in one step, api-notes A24).
/// EPLAN changing nothing is still committed, with no change listed; EPLAN renaming a terminal it was not to number rolls
/// back as EPLAN_OPERATION_FAILED.
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

    /// <summary>
    /// Numbers, then reads every terminal of the project back and compares it with what was there, terminal by terminal.
    /// Only the terminals the plan counted may change: given every terminal of the project with "only number marked
    /// ones", Renumber.Terminals also reordered the numbered strip +A1-XD1 (Large test, 2026-10-21, api-notes A24). Any
    /// other rename, in scope or not, is a failed read-back, which rolls everything back (coding standards 9.3, rule 9).
    /// </summary>
    private Numbered Renumber(TerminalRenumberRequest request, List<PlacedFunction> scope)
    {
        string path = request.Project.Path;
        IReadOnlyList<PlacedFunction> before = request.Strips is null ? scope : renumberer.ReadTerminals(path, null) ?? [];
        var planned = new HashSet<string>(scope.Where(request.IsToNumber).Select(t => t.Id), StringComparer.Ordinal);
        renumberer.Renumber(path, request.Strips, request.OnlyUnnumbered, request.Scheme, request.Start, request.Step);
        IReadOnlyList<PlacedFunction> after = renumberer.ReadTerminals(path, null) ?? [];
        (List<RenamedObject> unplanned, int othersUnmatched) = TerminalRenumberRequest.Changes(before.Where(t => !planned.Contains(t.Id)), after);
        if (unplanned.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                $"EPLAN also renamed {Count(unplanned.Count)} that were not to be numbered, so nothing was numbered. metadata.unplanned lists them (before -> after); "
                + "ask the engineer to number the terminals in EPLAN, or give only the strips to renumber in 'strips' with only_unnumbered false.",
                retryable: false,
                new Dictionary<string, string>
                {
                    ["project"] = request.Project.Name,
                    ["unplanned"] = string.Join("\n", unplanned.Take(TerminalRenumberRequest.MaxListed).Select(c => c.From + " -> " + c.To)),
                    ["unplannedCount"] = unplanned.Count.ToString(CultureInfo.InvariantCulture),
                });
        }

        (List<RenamedObject> changed, int unmatched) = TerminalRenumberRequest.Changes(before.Where(t => planned.Contains(t.Id)), after);
        IEnumerable<PlacedFunction> inScope = request.Strips is null ? after : request.Scope(renumberer);
        List<string> still = [.. inScope.Select(t => t.Name).Where(TerminalRenumberRequest.Unnumbered).Distinct(StringComparer.Ordinal)];
        return new Numbered(changed, unmatched + othersUnmatched, still);
    }

    private static string Count(int terminals) => terminals.ToString(CultureInfo.InvariantCulture) + (terminals == 1 ? " terminal" : " terminals");

    private sealed class Numbered(List<RenamedObject> changed, int unmatched, List<string> stillUnnumbered)
    {
        public List<RenamedObject> Changed { get; } = changed;

        public int Unmatched { get; } = unmatched;

        public List<string> StillUnnumbered { get; } = stillUnnumbered;
    }
}
