using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CableRenumberPlan"/>: checks the scheme against the project's cable numbering schemes,
/// finds the cables in scope and returns one change for the whole numbering, since only the scheme decides the new tags
/// (docs/tools/eplan_cable_renumber.md, section 5), with the counts in the details and the fingerprint. Nothing to
/// number: no change, so nothing is asked. Reads only.
/// </summary>
public sealed class CableRenumberPlanOperation(IProjectCatalog catalog, IDeviceReader devices, ICableRenumberer renumberer) : Operation<CableRenumberArgs, WritePlan>
{
    public override string Name => OperationNames.CableRenumberPlan;

    protected override WritePlan Execute(CableRenumberArgs args)
    {
        CableRenumberRequest request = CableRenumberRequest.Check(args, catalog, renumberer);
        List<PlacedFunction> scope = request.Scope(renumberer, devices);
        int cables = request.ToNumber(scope);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = cables == 0
                ? []
                : [new PlannedChange { Action = "update", Kind = "cables", Name = request.Project.Name, Property = "numbering", OldValue = request.Describe(cables) }],
            Fingerprint = CableRenumberRequest.FingerprintOf(scope),
            Details = new Dictionary<string, string>
            {
                ["cables"] = Number(cables),
                ["cableCount"] = request.Cables is null ? string.Empty : Number(request.Cables.Count),
                ["keepExisting"] = request.KeepExisting ? "true" : "false",
                ["scheme"] = request.Scheme,
                ["start"] = Number(request.Start),
                ["step"] = Number(request.Step),
            },
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
