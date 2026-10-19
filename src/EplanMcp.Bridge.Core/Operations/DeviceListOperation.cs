using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DeviceList"/> (docs/tools/eplan_device_list.md). EPLAN narrows the functions by page and
/// category; the rest happens here on plain records, keeping EPLAN's order: functions without a name are skipped,
/// auxiliary functions only when asked for, then the part and tag filters, then the slice.
/// </summary>
public sealed class DeviceListOperation(IProjectCatalog catalog, IDeviceReader devices) : Operation<DeviceListArgs, ListData<DeviceListItem>>
{
    /// <summary>
    /// Categories offered when <see cref="DeviceListArgs.Category"/> is not one: the enum has 183 values, too many for
    /// an error message. All are names of <c>Base.Enums.FunctionCategory</c> in EPLAN 2026 (reflection dump).
    /// </summary>
    public static readonly IReadOnlyList<string> CommonCategories =
    [
        "Coil", "NoContact", "NcContact", "ChangeOverContact", "Motor", "CircuitBreaker", "MotorOverloadSwitch", "Overload",
        "SafetyFuse", "Switch", "Sensor", "SignalLamp", "Transformer", "Converter", "Valve", "Terminal", "Plug", "PLCBox",
        "PLCTerminal", "Cable", "Blackbox",
    ];

    public override string Name => OperationNames.DeviceList;

    protected override ListData<DeviceListItem> Execute(DeviceListArgs args)
    {
        string? category = Given(args.Category);
        if (category is not null && !devices.IsCategory(category))
        {
            string violation = $"'category' is not an EPLAN function category. Common ones: {string.Join(", ", CommonCategories)}.";
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "Invalid arguments: " + violation,
                retryable: false,
                new Dictionary<string, string> { ["violations"] = violation });
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? page = Given(args.Page);
        IReadOnlyList<FunctionRecord> functions = devices.ReadFunctions(project.Path, page, category)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "The project has no page with this full name. Call eplan_page_list to see the page names and pass one exactly as listed.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = page! });

        string? text = Given(args.TagContains);
        List<FunctionRecord> matching =
        [
            .. functions.Where(f => f.Name.Length > 0
                && (f.IsMain || args.IncludeAuxiliary)
                && (args.HasPart is not { } hasPart || (f.Parts > 0) == hasPart)
                && (text is null || DeviceTags.Contains(f.Name, f.VisibleName, text))),
        ];

        return new ListData<DeviceListItem>
        {
            Project = project.Name,
            Items =
            [
                .. Paging.Slice(matching, args).Select(f => new DeviceListItem
                {
                    Tag = f.Name,
                    VisibleTag = DeviceTags.Shown(f.VisibleName),
                    Main = f.IsMain,
                    Category = f.Category,
                    Page = f.Page,
                    Parts = f.Parts,
                }),
            ],
            Total = matching.Count,
        };
    }
}
