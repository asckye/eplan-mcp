using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ConnectionList"/> (docs/tools/eplan_connection_list.md). The device is found as device.get
/// finds it: by its full tag, else by a shown tag that one device has. The adapter narrows the connections to that
/// device and to the page; here they are sliced in EPLAN's order and turned into contract items. Listing a whole
/// project is not offered: the sample project has 2060 connections, which is an export (tool spec 2).
/// </summary>
public sealed class ConnectionListOperation(IProjectCatalog catalog, IDeviceReader devices, IConnectionReader connections)
    : Operation<ConnectionListArgs, ListData<ConnectionListItem>>
{
    public override string Name => OperationNames.ConnectionList;

    protected override ListData<ConnectionListItem> Execute(ConnectionListArgs args)
    {
        string? device = Given(args.Device);
        string? page = Given(args.Page);
        if (device is null && page is null)
        {
            const string Violation = "'device' or 'page' is required: a device tag such as -K1, a full page name such as =GAA+A1&EFS1/1, or both.";
            throw InvalidArgument(Violation);
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? tag = device is null ? null : FullTag(project.Path, device);
        IReadOnlyList<ConnectionRecord> found = connections.ReadConnections(project.Path, tag, page)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "The project has no page with this full name. Call eplan_page_list to see the page names and pass one exactly as listed.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = page! });

        return new ListData<ConnectionListItem>
        {
            Project = project.Name,
            Items = [.. Paging.Slice(found, args).Select(ToContract)],
            Total = found.Count,
        };
    }

    /// <summary>The full tag itself when a main function has it (ordinal, as EPLAN names it); otherwise the one device showing it.</summary>
    private string FullTag(string projectPath, string device)
    {
        IReadOnlyList<FunctionName> mainFunctions = devices.ReadMainFunctionNames(projectPath);
        return mainFunctions.Any(f => string.Equals(f.Name, device, StringComparison.Ordinal))
            ? device
            : DeviceTags.FindByShownTag(mainFunctions, device, "device")
                ?? throw new BridgeErrorException(
                    ErrorReason.DeviceNotFound,
                    "No device in the project has this tag. Call eplan_device_list with 'tag_contains' to find the device's full tag.",
                    retryable: false,
                    new Dictionary<string, string> { ["device"] = device });
    }

    /// <summary>A 3D connection has no ends or page to give (S6), whatever the record holds.</summary>
    internal static ConnectionListItem ToContract(ConnectionRecord connection) => connection.Is3D
        ? new ConnectionListItem { Kind = ConnectionListItem.Kind3D, CrossSection = connection.CrossSection, Colour = connection.Colour, Designation = connection.Designation }
        : new ConnectionListItem
        {
            From = End(connection.StartDevice, connection.StartPin),
            To = End(connection.EndDevice, connection.EndPin),
            Page = connection.Page,
            CrossSection = connection.CrossSection,
            Colour = connection.Colour,
            Designation = connection.Designation,
            Kind = ConnectionListItem.Kind2D,
        };

    /// <summary>Empty names are no names; an end with neither a function nor a connection point name is not connected.</summary>
    internal static ConnectionEnd? End(string? device, string? pin)
    {
        string? function = string.IsNullOrEmpty(device) ? null : device;
        string? point = string.IsNullOrEmpty(pin) ? null : pin;
        return function is null && point is null ? null : new ConnectionEnd { Device = function, Pin = point };
    }
}
