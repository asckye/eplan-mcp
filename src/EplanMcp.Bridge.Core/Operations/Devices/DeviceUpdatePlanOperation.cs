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
        var plan = new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Rename ? new[] { new PlannedChange { Action = "update", Kind = "device", Name = request.Device.Tag, Property = "tag", OldValue = request.Device.Tag, NewValue = request.NewTag } } : [],
                .. request.Part is { } part ? new[] { PartChange(request, part) } : [],
                .. request.UpdateFromParts || DeviceUpdateRequest.TransfersCableCores(request) ? new[] { new PlannedChange { Action = "update", Kind = "part data", Name = request.NewTag, Property = "from parts" } } : []],
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
        if (request.OldPart is { } old)
        {
            plan.Details!["replacedPart"] = Protocol.BridgeJson.FromObject(new ReplacedPart
            { PartNumber = old.PartNumber, Variant = old.Variant, Count = old.Count }).ToString(Newtonsoft.Json.Formatting.None);
        }
        if (request.Position is int position && position < request.Main!.Parts.Count - 1)
        {
            plan.Details!["warning"] = "Later references are re-added in order. Their overridden properties reset to the part values.";
        }
        if (DeviceUpdateRequest.TransfersCableCores(request))
        {
            plan.Details!["cableCores"] = CableCoresHint;
        }
        return plan;
    }

    internal const string CableCoresHint = "The part data was transferred in the same step, so the cable's wires sit on the part's cores (A167). " +
        "If the check still reports more functions than the part provides, a wire found no fitting core: " +
        "with three wires without PE potential a 3G part left one over, a 4G part none.";

    private static PlannedChange PartChange(DeviceUpdateRequest request, PartSearchItem part) => request.OldPart is { } old
        ? new PlannedChange
        {
            Action = "update",
            Kind = "part",
            Name = request.NewTag,
            OldValue = FormattableString.Invariant($"{old.PartNumber} / {old.Variant} ×{old.Count}"),
            NewValue = FormattableString.Invariant($"{part.PartNumber} / {part.Variant} ×{request.Count}")
        }
        : new PlannedChange { Action = "add", Kind = "part", Name = request.NewTag, Property = part.Variant, NewValue = part.PartNumber };
}
