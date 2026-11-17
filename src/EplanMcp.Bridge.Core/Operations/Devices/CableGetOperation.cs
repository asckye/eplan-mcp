using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CableGet"/> (docs/tools/eplan_cable_get.md, section 5). The cable is found as device.get
/// finds a device and must then be a cable; a shielding or any other device is INVALID_ARGUMENT naming its category.
/// </summary>
public sealed class CableGetOperation(IProjectCatalog catalog, IDeviceReader devices, ICableReader cables)
    : Operation<CableGetArgs, CableGetData>
{
    /// <summary>The most sources, and the most targets, returned (tool spec 3).</summary>
    public const int MaxEnds = 20;

    public override string Name => OperationNames.CableGet;

    protected override CableGetData Execute(CableGetArgs args)
    {
        string tag = Given(args.Cable) ?? throw InvalidArgument("'cable' is required: a cable tag such as -WG2 or +B1-WG2.");
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? language = Given(args.Language);
        CableRecord record = DeviceTags.Find(
            devices,
            project.Path,
            tag,
            "cable",
            t => cables.ReadCable(project.Path, t, language),
            "No device in the project has this tag. Call eplan_device_list with category Cable to find the full tags of the cables.");
        if (record.Cable is not { } cable)
        {
            // The category is EPLAN's enum name, not project data, so the message may carry it.
            BridgeErrorException error = InvalidArgument($"'cable' names a device of category {record.Category}, not a cable. Call eplan_device_get for it.");
            error.Error.Metadata!["category"] = record.Category;
            throw error;
        }

        cable.VisibleTag = DeviceTags.Shown(cable.VisibleTag);
        cable.Sources = [.. cable.Sources.Distinct(StringComparer.Ordinal).Take(MaxEnds)];
        cable.Targets = [.. cable.Targets.Distinct(StringComparer.Ordinal).Take(MaxEnds)];
        return new CableGetData
        {
            Project = project.Name,
            Cable = cable,
            Cores = [.. Paging.Slice(record.Cores, args).Select(ConnectionListOperation.ToContract)],
            Total = record.Cores.Count,
        };
    }
}
