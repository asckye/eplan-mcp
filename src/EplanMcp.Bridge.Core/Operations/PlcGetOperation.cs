using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PlcGet"/> (docs/tools/eplan_plc_get.md, section 5). The PLC box is found as device.get finds
/// a device and must then be one; any other device is INVALID_ARGUMENT naming its category. Every channel object is kept
/// in EPLAN's order, same names included, and each connection end at the box is given to its channel here.
/// </summary>
public sealed class PlcGetOperation(IProjectCatalog catalog, IDeviceReader devices, IPlcReader plcs)
    : Operation<PlcGetArgs, PlcGetData>
{
    /// <summary>The most connection ends returned per channel, as for a terminal of a strip.</summary>
    public const int MaxEnds = 10;

    public override string Name => OperationNames.PlcGet;

    protected override PlcGetData Execute(PlcGetArgs args)
    {
        string tag = Given(args.Plc) ?? throw InvalidArgument("'plc' is required: a PLC box tag such as -KE1 or =K1+A2-KE1.");
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? language = Given(args.Language);
        PlcRecord record = DeviceTags.Find(
            devices,
            project.Path,
            tag,
            "plc",
            t => plcs.ReadPlc(project.Path, t, language),
            "No device in the project has this tag. Call eplan_device_list with category PLCBox to find the full tags of the PLC boxes.");
        if (record.Plc is not { } plc)
        {
            // The category is EPLAN's enum name, not project data, so the message may carry it.
            BridgeErrorException error = InvalidArgument($"'plc' names a device of category {record.Category}, not a PLC box. Call eplan_device_get for it.");
            error.Error.Metadata!["category"] = record.Category;
            throw error;
        }

        plc.VisibleTag = DeviceTags.Shown(plc.VisibleTag);
        List<ConnectionEnd>[] ends = [.. record.Channels.Select(_ => new List<ConnectionEnd>())];
        foreach (PlcConnectionRecord connection in record.Connections)
        {
            if (ConnectionListOperation.End(connection.Other.Device, connection.Other.Pin) is { } end)
            {
                foreach (int channel in Owners(record.Channels, connection))
                {
                    ends[channel].Add(end);
                }
            }
        }

        for (int i = 0; i < ends.Length; i++)
        {
            record.Channels[i].Connected = [.. ends[i].Take(MaxEnds)];
        }

        return new PlcGetData
        {
            Project = project.Name,
            Plc = plc,
            Channels = Paging.Slice(record.Channels, args),
            Total = record.Channels.Count,
            Connected = record.Channels.Count(c => c.Connected.Count > 0),
        };
    }

    /// <summary>
    /// The channels an end at the box belongs to: the channel object EPLAN gives, else the channels of the same name. A
    /// name can be several channel objects (input and output, main and auxiliary representation), so those on the
    /// connection's page are taken when there are any; otherwise every one of that name.
    /// </summary>
    internal static IEnumerable<int> Owners(List<PlcChannel> channels, PlcConnectionRecord connection)
    {
        if (connection.Channel is int known && known >= 0 && known < channels.Count)
        {
            return [known];
        }

        int[] named = [.. Enumerable.Range(0, channels.Count).Where(i => !string.IsNullOrEmpty(connection.Function) && string.Equals(channels[i].Name, connection.Function, StringComparison.Ordinal))];
        int[] onPage = [.. named.Where(i => connection.Page is not null && string.Equals(channels[i].Page, connection.Page, StringComparison.Ordinal))];
        return onPage.Length > 0 ? onPage : named;
    }
}
