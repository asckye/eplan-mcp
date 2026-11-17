using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TerminalRenumberPlan"/>: checks the scheme against EPLAN's terminal numbering schemes, finds
/// the scope and the terminals to number and returns one change for the whole numbering, since only the scheme and the
/// order in the strips decide the new names (docs/tools/eplan_terminal_renumber.md, section 5), with the counts in the
/// details and the fingerprint. Nothing to number: no change, so nothing is asked. Reads only.
/// </summary>
public sealed class TerminalRenumberPlanOperation(IProjectCatalog catalog, ITerminalRenumberer renumberer) : Operation<TerminalRenumberArgs, WritePlan>
{
    public override string Name => OperationNames.TerminalRenumberPlan;

    protected override WritePlan Execute(TerminalRenumberArgs args)
    {
        TerminalRenumberRequest request = TerminalRenumberRequest.Check(args, catalog, renumberer);
        List<PlacedFunction> scope = request.Scope(renumberer);
        int terminals = request.ToNumber(scope);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = terminals == 0
                ? []
                : [new PlannedChange { Action = "update", Kind = "terminals", Name = request.Project.Name, Property = "numbering", OldValue = request.Describe(terminals) }],
            Fingerprint = TerminalRenumberRequest.FingerprintOf(scope),
            Details = new Dictionary<string, string>
            {
                ["terminalCount"] = Number(terminals),
                ["stripCount"] = request.Strips is null ? string.Empty : Number(request.Strips.Count),
                ["onlyUnnumbered"] = request.OnlyUnnumbered ? "true" : "false",
                ["scheme"] = request.Scheme,
                ["start"] = Number(request.Start),
                ["step"] = Number(request.Step),
            },
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
