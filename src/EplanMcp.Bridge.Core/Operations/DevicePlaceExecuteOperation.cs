using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.DevicePlaceExecute"/>: checks the arguments and the plan's fingerprint again, then places the
/// part's device and gives all its functions the tag in one transaction, and reads back; nothing placed on the page, or a
/// tag EPLAN changed, rolls everything back (docs/tools/eplan_device_place.md, section 5). S20 found the placement taken
/// back by the SafetyPoint and undone by one Ctrl+Z after the commit (api-notes A26).
/// </summary>
public sealed class DevicePlaceExecuteOperation(
    IProjectCatalog catalog,
    IPageReader pages,
    IDeviceRenamer names,
    IPartsDatabase parts,
    IDevicePlacer placer,
    ITransactionScopeFactory transactions,
    ISymbolReader? symbols = null,
    Func<string, string, SymbolSearchItem, IDeviceSymbolPlacement>? openSymbol = null,
    IPlcReader? plc = null)
    : Operation<DevicePlaceArgs, DevicePlaceData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_device_place".</summary>
    public const string Tool = "eplan_device_place";

    public override string Name => OperationNames.DevicePlaceExecute;

    protected override DevicePlaceData Execute(DevicePlaceArgs args)
    {
        DevicePlaceRequest request;
        IDeviceSymbolPlacement? symbolSession = null;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one device.place.plan returned.");
            }

            request = DevicePlaceRequest.Check(args, catalog, pages, names, parts, placer, plc);
            WritePlan? symbolPlan = args.Library is null && args.LocationBox is null && args.TerminalStrip is null ? null : DeviceSymbolPlan.Create(args, request, symbols);
            if (!string.Equals(symbolPlan?.Fingerprint ?? request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(
                    ErrorReason.PlanStale,
                    "The objects on the page, or the devices using the tag, changed after the plan was made, so nothing was placed. Call eplan_device_place again for a new plan.",
                    retryable: false,
                    new Dictionary<string, string> { ["project"] = request.Project.Name });
            }

            request.CheckFree();
            if (symbolPlan is not null)
            {
                SymbolSearchItem symbol = Newtonsoft.Json.Linq.JObject.Parse(symbolPlan.Details!["symbol"]).ToObject<SymbolSearchItem>(BridgeJson.Serializer)!;
                symbolSession = openSymbol!(request.Project.Path, request.Page.Name, symbol);
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        if (symbolSession is not null)
        {
            // S41: Abort without CloseUndo leaves no empty step. Lists are already released (A55).
            Committed<DevicePlaceData> result = TransactionRunner.Run(symbolSession, Tool, () => PlaceSymbol(symbolSession, args, request.Tag, request.Page.Name), _ => true);
            result.Result.Project = request.Project.Name;
            result.Result.PageSizeKnown = request.Page.Width is not null;
            result.Result.Warning = result.Warning;
            return result.Result;
        }

        Committed<PlacedDevice> committed = TransactionRunner.Run(transactions, Tool, () => Place(request));
        PlacedDevice placed = committed.Result;
        return new DevicePlaceData
        {
            Project = request.Project.Name,
            Page = request.Page.Name,
            Part = request.Part,
            Variant = request.Variant,
            Tag = placed.MainName,
            VisibleTag = DeviceTags.Shown(placed.VisibleName),
            Category = placed.Category,
            Functions = placed.Names.Count,
            Unplaced = placed.Unplaced,
            PageSizeKnown = request.Page.Width is not null,
            Warning = committed.Warning,
        };
    }

    private static DevicePlaceData PlaceSymbol(IDeviceSymbolPlacement session, DevicePlaceArgs args, string tag, string page)
    {
        DevicePlaceData data = session.Place(args, tag);
        if (args.PlcConnectionPoint is { } point && (data.Plc is not { } channel || !DevicePlaceRequest.SameName(tag, channel.Name)
            || data.Page != page || !DevicePlaceRequest.SameName(tag, data.Tag) || channel.Page != page || data.Category != "PLCTerminal"
            || channel.Owner is not null && !DevicePlaceRequest.SameName(tag.Substring(0, tag.Length - point.Length - 1), channel.Owner)
            || channel.Address != args.PlcAddress || channel.ManualSymbolicAddress != args.PlcSymbolicAddress || channel.Text != args.PlcText))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "PLC point identity, owner, address or text readback differs; nothing was committed."); }
        if (args.LocationBox is not null && (data.LocationBox is not { } box || box.Structure != args.LocationBox
            || box.Outline != "dashed" || box.AffectedDevices.Length != 0 || !box.Bounds.SequenceEqual(new[] { args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value })))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Location box structure, outline or unaffected-device readback differs; nothing was committed."); }
        // Live T7c: names may lose the leading higher-level function (project structure settings); the strip
        // relation can stay null until the commit (S43), and eplan_terminal_strip_get confirms it afterwards.
        if (args.TerminalStrip is not null && (data.Terminal is not { } terminal || !DevicePlaceRequest.SameName(tag, data.Tag) || data.Page != page
            || !DevicePlaceRequest.SameName(tag.Substring(0, tag.LastIndexOf(':')), terminal.Strip) || terminal.Owner is not null && terminal.Owner != terminal.Strip || terminal.Number != args.TerminalNumber
            || !terminal.Position.SequenceEqual(new[] { args.X, args.Y })))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Terminal identity, ownership or position differs; nothing was committed."); }
        return data;
    }

    /// <summary>The tool spec's steps in the transaction: place and rename (one adapter call), then check what EPLAN did.</summary>
    private PlacedDevice Place(DevicePlaceRequest request)
    {
        PlacedDevice placed = placer.Place(request.Project.Path, request.Page.Name, request.Part, request.Variant, request.X, request.Y, request.Tag);
        if (placed.Names.Count == 0 || placed.OnPage == 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"EPLAN placed nothing on the page for part '{request.Part}', so nothing was changed: its macro may be missing or empty. Insert a window macro with eplan_macro_insert instead.",
                retryable: false,
                new Dictionary<string, string> { ["part"] = request.Part, ["functions"] = placed.Names.Count.ToString(CultureInfo.InvariantCulture) });
        }

        string? other = placed.Names.FirstOrDefault(name => !string.Equals(name, request.Tag, StringComparison.Ordinal));
        if (!string.Equals(placed.MainName, request.Tag, StringComparison.Ordinal) || other is not null)
        {
            string actual = string.Equals(placed.MainName, request.Tag, StringComparison.Ordinal) ? other! : placed.MainName;
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"EPLAN named the device '{actual}' instead of '{request.Tag}': devices of this kind do not use every part of the tag, so nothing was placed. Use the structure that similar devices have (eplan_device_list).",
                retryable: false,
                new Dictionary<string, string> { ["requested"] = request.Tag, ["actual"] = actual });
        }

        return placed;
    }

}
