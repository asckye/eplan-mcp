using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceGet"/> (docs/tools/eplan_device_get.md, section 5). The full tag is tried first,
/// because it is unique and EPLAN finds it with a name filter; only then the shown tag among all main functions, which
/// can match devices in several structures (api-notes A11).
/// </summary>
public sealed class DeviceGetOperation(IProjectCatalog catalog, IDeviceReader devices) : Operation<DeviceGetArgs, DeviceGetData>
{
    /// <summary>The most auxiliary functions returned; a PLC box can have hundreds (tool spec 3).</summary>
    public const int MaxFunctions = 50;

    public override string Name => OperationNames.DeviceGet;

    protected override DeviceGetData Execute(DeviceGetArgs args)
    {
        string tag = args.Tag?.Trim() ?? string.Empty;
        if (tag.Length == 0)
        {
            const string Violation = "'tag' is required: a device tag such as -K1 or =GAA+A1-FC1.";
            throw InvalidArgument(Violation);
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? language = Given(args.Language);
        DeviceRecord device = devices.ReadDevice(project.Path, tag, language, args.Detailed)
            ?? (DeviceTags.FindByShownTag(devices.ReadMainFunctionNames(project.Path), tag) is { } fullTag
                ? devices.ReadDevice(project.Path, fullTag, language, args.Detailed)
                : null)
            ?? throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                "No device in the project has this tag. Call eplan_device_list with 'tag_contains' to find the device's full tag.",
                retryable: false,
                new Dictionary<string, string> { ["tag"] = tag });

        return new DeviceGetData
        {
            Project = project.Name,
            Device = ToContract(device.Main),
            Functions = [.. device.Auxiliary.Take(MaxFunctions).Select(ToContract)],
            FunctionCount = device.Auxiliary.Count,
            Parts = device.Parts,
            Properties = args.Detailed ? device.Properties ?? [] : null,
        };
    }

    private static DeviceFunction ToContract(FunctionDetail function) => new()
    {
        Tag = function.Name,
        VisibleTag = DeviceTags.Shown(function.VisibleName),
        Category = function.Category,
        Page = function.Page,
        Text = function.Text,
        TextLanguage = function.TextLanguage,

        // EPLAN reports connection points without a designation as empty names (S6): nothing a model can use.
        Pins = [.. function.Pins.Where(pin => pin.Length > 0)],
    };
}
