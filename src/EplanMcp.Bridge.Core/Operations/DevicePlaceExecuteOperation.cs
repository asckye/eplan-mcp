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
            Committed<DevicePlaceData> result = TransactionRunner.Run(symbolSession, Tool, () =>
            {
                DevicePlaceData data = PlaceSymbol(symbolSession, args, request.Tag, request.Page.Name);
                data.Warning = LayoutGuard.Summary(request.LayoutWarnings.Concat(LayoutGuard.AfterWriting(request.Page.Layout!, (symbolSession as LayoutGuard.IMeasurement)?.ReadLayout())));
                return data;
            }, _ => true);
            string? warning = result.Warning;
            if ((args.PlcConnectionPoint is not null || DeviceSymbolPlan.BlackBox(args) || DeviceSymbolPlan.ConnectionPoint(args) || DeviceSymbolPlan.BusBarDefinition(args) || DeviceSymbolPlan.BusBarPoint(args)) && symbolSession is IDeviceSymbolCommittedReadback committedReadback)
            {
                try
                { committedReadback.ReadCommitted(args, result.Result); }
                catch (Exception failure)
                {
                    // Commit succeeded: a lookup/verification failure cannot make its outcome unknown.
                    string readbackWarning = failure is BridgeErrorException { Error.Execution: ExecutionState.Committed } known
                        ? known.Error.Message
                        : args.PlcConnectionPoint is not null ? "The PLC point was committed, but its identity, card ownership, address, symbolic address, text, plug designation, native references and displays could not be fully verified. Inspect the page and eplan_plc_get before repeating the placement."
                        : "The symbol was committed, but its native readback could not be fully verified. Inspect the page and device before repeating the placement.";
                    warning = warning is null ? readbackWarning : warning + " " + readbackWarning;
                }
            }
            result.Result.Project = request.Project.Name;
            result.Result.PageSizeKnown = request.Page.Width is not null;
            result.Result.Warning = LayoutGuard.Summary([result.Result.Warning, warning]);
            return result.Result;
        }

        Committed<PlacedDevice> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            PlacedDevice data = Place(request);
            request.LayoutWarnings.AddRange(LayoutGuard.AfterWriting(request.Page.Layout!));
            return data;
        }, verifyCommitted: data =>
        {
            int expected = data.Names.Count;
            placer.ReadCommitted(request.Project.Path, data);
            Verify(data, request);
            if (data.Names.Count != expected)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, $"Committed device has {data.Names.Count} functions instead of {expected}; its functions did not retain their device tag."); }
        });
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
            UnplacedFunctions = placed.UnplacedFunctions,
            PageSizeKnown = request.Page.Width is not null,
            Warning = LayoutGuard.Summary(request.LayoutWarnings.Append(committed.Warning)),
        };
    }

    private static DevicePlaceData PlaceSymbol(IDeviceSymbolPlacement session, DevicePlaceArgs args, string tag, string page)
    {
        DevicePlaceData data = session.Place(args, tag);
        if (args.PlcConnectionPoint is not null && (data.Plc is not { } channel || !DevicePlaceRequest.SameName(tag, channel.Name)
            || data.Page != page || !DevicePlaceRequest.SameName(tag, data.Tag) || channel.Page != page || data.Category != "PLCTerminal"
            || channel.Owner is not null && !DevicePlaceRequest.SameName(DevicePlcPlan.Parent(args, tag), channel.Owner)
            || args.PlcPlugDesignation is not null && channel.PlugDesignation != args.PlcPlugDesignation
            || args.PlcAddress is not null && channel.Address != args.PlcAddress
            || args.PlcSymbolicAddress is not null && channel.ManualSymbolicAddress != args.PlcSymbolicAddress
            || args.PlcText is not null && channel.Text != args.PlcText))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "PLC point identity, owner, plug, address or text readback differs; nothing was committed."); }
        if (args.LocationBox is not null && (data.LocationBox is not { } box || box.Structure != args.LocationBox
            || box.Outline != "dashed" || box.AffectedDevices.Length != 0 || !box.Bounds.SequenceEqual(new[] { args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value })))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Location box structure, outline or unaffected-device readback differs; nothing was committed."); }
        // Live T7c: names may lose the leading higher-level function (project structure settings); the strip
        // relation can stay null until the commit (S43), and eplan_terminal_strip_get confirms it afterwards.
        if (args.TerminalStrip is not null && (data.Terminal is not { } terminal || !DevicePlaceRequest.SameName(tag, data.Tag) || data.Page != page
            || !DevicePlaceRequest.SameName(tag.Substring(0, tag.LastIndexOf(':')), terminal.Strip) || terminal.Owner is not null && terminal.Owner != terminal.Strip || terminal.Number != args.TerminalNumber
            || !terminal.Position.SequenceEqual(new[] { args.X, args.Y }) || args.TerminalStripPosition is { } position
                && (terminal.StripPosition is null || !terminal.StripPosition.SequenceEqual(position) || terminal.StripText != args.TerminalStripText)))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Terminal identity, ownership or position differs; nothing was committed."); }
        if (args.DisplayProperties is { Count: > 0 })
        { DeviceSymbolDisplays.Verify(args, tag, page, data.Plc, session.ReadDisplays()); }
        return data;
    }

    /// <summary>The tool spec's steps in the transaction: place and rename (one adapter call), then check what EPLAN did.</summary>
    private PlacedDevice Place(DevicePlaceRequest request)
    {
        PlacedDevice placed = placer.Place(request.Project.Path, request.Page.Name, request.Part, request.Variant, request.X, request.Y, request.Tag);
        Verify(placed, request, committed: false);
        return placed;
    }

    private static void Verify(PlacedDevice placed, DevicePlaceRequest request, bool committed = true)
    {
        if (placed.Names.Count == 0 || placed.OnPage == 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"EPLAN placed nothing on the page for part '{request.Part}', so nothing was changed: its macro may be missing or empty. Insert a window macro with eplan_macro_insert instead.",
                retryable: false,
                new Dictionary<string, string> { ["part"] = request.Part, ["functions"] = placed.Names.Count.ToString(CultureInfo.InvariantCulture) });
        }

        // A82: PLC template functions carry the device tag followed by a connection-point suffix.
        bool Belongs(string name) => string.Equals(name, request.Tag, StringComparison.Ordinal) || name.StartsWith(request.Tag + ":", StringComparison.Ordinal);
        // Live 2026-11-03 (A85): before the commit the other functions' wrappers still show the old tag; they are read again by
        // the new tag after the commit, where every function must carry it.
        string? other = committed ? placed.Names.FirstOrDefault(name => !Belongs(name)) : null;
        if (request.Tag.Length > 0 && (!Belongs(placed.MainName) || other is not null))
        {
            string actual = Belongs(placed.MainName) ? other! : placed.MainName;
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"EPLAN named the device '{actual}' instead of '{request.Tag}': devices of this kind do not use every part of the tag, so nothing was placed. Use the structure that similar devices have (eplan_device_list).",
                retryable: false,
                new Dictionary<string, string> { ["requested"] = request.Tag, ["actual"] = actual });
        }
    }

}
