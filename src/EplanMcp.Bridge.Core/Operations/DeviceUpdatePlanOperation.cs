using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceUpdatePlan"/>: finds the device and what a rename touches, refuses a tag in use, and
/// returns one <c>update</c> change with the counts for the approval form (docs/tools/eplan_device_update.md). Reads only.
/// </summary>
public sealed class DeviceUpdatePlanOperation(IProjectCatalog catalog, IDeviceReader reader, IDeviceRenamer renamer,
    IPartsDatabase? parts = null, IDevicePartAssigner? assigner = null) : Operation<DeviceUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.DeviceUpdatePlan;

    protected override WritePlan Execute(DeviceUpdateArgs args)
    {
        var request = DeviceUpdateRequest.Check(args, catalog, reader, renamer, parts, assigner);
        request.CheckFree();
        string[] pages = [.. request.Pages];
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Rename ? new[] { new PlannedChange { Action = "update", Kind = "device", Name = request.Device.Tag, Property = "tag", OldValue = request.Device.Tag, NewValue = request.NewTag } } : [],
                .. request.Part is { } part ? new[] { new PlannedChange { Action = "add", Kind = "part", Name = request.NewTag, Property = part.Variant, NewValue = part.PartNumber } } : [],
                .. request.UpdateFromParts ? new[] { new PlannedChange { Action = "update", Kind = "part data", Name = request.NewTag, Property = "from parts" } } : []],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                ["functions"] = request.Device.Functions.Count.ToString(CultureInfo.InvariantCulture),
                ["pageCount"] = pages.Length.ToString(CultureInfo.InvariantCulture),
                ["pages"] = string.Join("\n", pages.Take(DeviceUpdateRequest.MaxPages)),
                ["tag"] = request.ResultTag,
                ["previousTag"] = request.PreviousTag,
                ["part"] = request.Part?.PartNumber ?? string.Empty,
                ["variant"] = request.Part?.Variant ?? string.Empty,
                ["count"] = request.Count.ToString(CultureInfo.InvariantCulture),
            },
        };
    }
}
