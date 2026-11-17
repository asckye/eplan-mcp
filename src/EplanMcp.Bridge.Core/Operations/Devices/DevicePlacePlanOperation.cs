using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DevicePlacePlan"/>: checks the page, the point, the part and its variant, refuses a tag in
/// use, and returns one <c>create</c> change for the dry run and the approval form (docs/tools/eplan_device_place.md,
/// section 5). Reads only: the parts database is opened and closed as eplan_part_search does it (A27).
/// </summary>
public sealed class DevicePlacePlanOperation(IProjectCatalog catalog, IPageReader pages, IDeviceRenamer names, IPartsDatabase parts, IDevicePlacer placer, ISymbolReader? symbols = null, IPlcReader? plc = null)
    : Operation<DevicePlaceArgs, WritePlan>
{
    public override string Name => OperationNames.DevicePlacePlan;

    protected override WritePlan Execute(DevicePlaceArgs args)
    {
        var request = DevicePlaceRequest.Check(args, catalog, pages, names, parts, placer, plc);
        request.CheckFree();
        return Create(args, request, symbols);
    }

    internal static WritePlan Create(DevicePlaceArgs args, DevicePlaceRequest request, ISymbolReader? symbols)
    {
        if (!DevicePlacementRoute.UsesPart(args) || args.Library is not null)
        { return DeviceSymbolPlan.Create(args, request, symbols); }
        var plan = new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [request.Change()],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                ["part"] = request.Part,
                ["layoutWarnings"] = string.Join("\n", request.LayoutWarnings),
                ["variant"] = request.Variant,
                ["tag"] = request.Tag,
                ["pageSizeKnown"] = request.Page.Width is null ? "false" : "true",
            },
        };
        if (args.TerminalStrip is not null)
        {
            plan.Details!["terminal"] = Protocol.BridgeJson.FromObject(DeviceTerminalPlan.Describe(args, request, plan.Changes)).ToString(Newtonsoft.Json.Formatting.None);
            plan.Fingerprint = Fingerprint.Of([plan.Fingerprint, plan.Details["terminal"], Protocol.BridgeJson.FromObject(args.StripDefinition).ToString(Newtonsoft.Json.Formatting.None)]);
        }
        return plan;
    }
}
