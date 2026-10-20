using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceRenumberPlan"/>: finds the scope and the devices to number and returns one change for
/// the whole renumbering, since only EPLAN's rules decide the new tags (M4 design Q7), with the devices in the details and
/// the fingerprint (docs/tools/eplan_device_renumber.md, section 5). Nothing to number: no change, so nothing is asked.
/// Reads only.
/// </summary>
public sealed class DeviceRenumberPlanOperation(IProjectCatalog catalog, IDeviceRenumberer renumberer) : Operation<DeviceRenumberArgs, WritePlan>
{
    public override string Name => OperationNames.DeviceRenumberPlan;

    protected override WritePlan Execute(DeviceRenumberArgs args)
    {
        DeviceRenumberRequest request = DeviceRenumberRequest.Check(args, catalog);
        List<PlacedFunction> scope = request.Scope(renumberer);
        List<string> devices = request.Devices(scope);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = devices.Count == 0
                ? []
                : [new PlannedChange { Action = "update", Kind = "devices", Name = request.Project.Name, Property = "device tags", OldValue = request.Describe(devices.Count) }],
            Fingerprint = DeviceRenumberRequest.FingerprintOf(scope),
            Details = new Dictionary<string, string>
            {
                ["devices"] = string.Join("\n", devices.Take(DeviceRenumberRequest.MaxListed)),
                ["deviceCount"] = Number(devices.Count),
                ["pageCount"] = request.Pages is null ? string.Empty : Number(request.Pages.Count),
                ["onlyUnnumbered"] = request.OnlyUnnumbered ? "true" : "false",
                ["scheme"] = request.Scheme ?? string.Empty,
                ["start"] = Number(request.Start),
                ["step"] = Number(request.Step),
            },
        };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
