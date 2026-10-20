using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceUpdatePlan"/>: finds the device and what a rename touches, refuses a tag in use, and
/// returns one <c>update</c> change with the counts for the approval form (docs/tools/eplan_device_update.md). Reads only.
/// </summary>
public sealed class DeviceUpdatePlanOperation(IProjectCatalog catalog, IDeviceReader reader, IDeviceRenamer renamer) : Operation<DeviceUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.DeviceUpdatePlan;

    protected override WritePlan Execute(DeviceUpdateArgs args)
    {
        var request = DeviceUpdateRequest.Check(args, catalog, reader, renamer);
        request.CheckFree();
        string[] pages = [.. request.Pages];
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new PlannedChange { Action = "update", Kind = "device", Name = request.Device.Tag, Property = "tag", OldValue = request.Device.Tag, NewValue = request.NewTag }],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                ["functions"] = request.Device.Functions.Count.ToString(CultureInfo.InvariantCulture),
                ["pageCount"] = pages.Length.ToString(CultureInfo.InvariantCulture),
                ["pages"] = string.Join("\n", pages.Take(DeviceUpdateRequest.MaxPages)),
            },
        };
    }
}
