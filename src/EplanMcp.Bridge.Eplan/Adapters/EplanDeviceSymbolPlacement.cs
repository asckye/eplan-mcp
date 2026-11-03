using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A13/A52-A55: resolve before writing, read returned objects inside Transaction; never dispose owned objects.</summary>
internal sealed partial class EplanDeviceSymbolPlacement : IDeviceSymbolPlacement, LayoutGuard.IMeasurement, IDeviceSymbolCommittedReadback
{
    private readonly string _projectPath;
    private readonly string _pageName;
    private readonly SymbolSearchItem _symbol;
    private IReadOnlyList<DeviceSymbolDisplay> _placedDisplays = [];
    private long? _placedFunctionId;
    private long? _plcCardId;
    private LayoutGuard.Model? _placedLayout;
    public LayoutGuard.Model ReadLayout() => _placedLayout ?? EplanLayout.Measure(_projectPath, _pageName, []);

    public EplanDeviceSymbolPlacement(string path, string page, SymbolSearchItem symbol)
    {
        MainThread.Assert();
        _projectPath = path;
        _pageName = page;
        _symbol = symbol;
    }

    public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(_projectPath), undoDescription);

    public DevicePlaceData Place(DevicePlaceArgs args, string tag)
    {
        try
        {
            MainThread.Assert();
            Project project = EplanProjects.ByPath(_projectPath);
            Page page = project.Pages.Single(p => string.Equals(p.Name, _pageName, StringComparison.Ordinal));
            SymbolVariant variant = project.SymbolLibraries.Single(l => l.Name == _symbol.Library).Symbols.Single(s => s.Name == _symbol.Name)
                .Variants.Single(v => v.VariantNr == _symbol.Variant);
            return args.TerminalStrip is not null ? CreateTerminal(args, tag, project, page, variant)
                : args.LocationBox is null ? Create(args, tag, project, page, variant) : CreateLocationBox(args, project, page, variant);
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException && new EplanExceptionMapper().Map(ex) is null)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN could not create the requested symbol or displays. Nothing was committed; read the page and re-plan.", ex);
        }
    }

    private DevicePlaceData Create(DevicePlaceArgs args, string tag, Project project, Page page, SymbolVariant variant)
    {
        MainThread.Assert();
#pragma warning disable CA2000 // A39/A55: returned persistent functions belong to EPLAN, including on Abort.
        Function function = CreateFunction(args, tag, project, page, variant);
#pragma warning restore CA2000
        function.Name = tag;
        // Live T7d: a standalone contact is not a main function by default, so device reads cannot find it. The tag is
        // new (ALREADY_EXISTS otherwise), so this function is the device's main function, like a pushbutton's contact.
        // A PLC point belongs to its existing PLC box (its own main function), so it stays as EPLAN places it.
        if (args.PlcConnectionPoint is null)
        {
            if (!function.IsMainFunction)
            { function.IsMainFunction = true; }
            Require(function.IsMainFunction);
        }
        function.Location = new PointD(DrawingCoordinates.MillimetresToApi(args.X), DrawingCoordinates.MillimetresToApi(args.Y));
        // Customer redraw 2026-11-02: with a visible name the symbol's default property arrangement shows the tag where the
        // editor would (left of a contact). A caller that places its own tag text keeps the empty visible name, or the tag
        // would show twice; display_properties switch to a user-defined arrangement.
        if (args.DisplayProperties?.Any(p => p.PropertyId is 20214 or 20213 or 20211 or 20215) != true)
        { EplanVisibleNames.Adjust(page, function); }
        // A55: the only cached list is released while unwinding Place, BEFORE TransactionRunner can Abort.
        using FunctionPropertyList properties = function.Properties;
        WritePlcPoint(properties, args);
        string[]? requested = args.ConnectionDesignations;
        for (int i = 0; i < (requested?.Length ?? 0); i++)
        { properties[(Properties.Function)20022, i + 1] = requested![i]; }
        // Customer redraw: a PLC point's first table row (20029, read-only) shows these descriptions.
        string[]? descriptions = args.ConnectionDescriptions;
        for (int i = 0; i < (descriptions?.Length ?? 0); i++)
        { properties[(Properties.Function)20023, i + 1] = descriptions![i]; }
        // A78: local placements are independent of the symbol variant (documentation only).
        if (args.PlcConnectionPoint is not null && args.DisplayProperties is { Count: > 0 })
        { function.SwitchLocalPropertyPlacements(true); Require(function.UseLocalPropertyPlacements); }
        string? visible = null;
        foreach (DeviceDisplayProperty display in args.DisplayProperties ?? [])
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            using AnyPropertyId id = display.PropertyId;
#pragma warning disable CA2000 // PropertyPlacement is EPLAN-owned (A39/A53).
            var placement = new PropertyPlacement();
#pragma warning restore CA2000
            placement.Create(function, id);
            placement.Location = new PointD(DrawingCoordinates.MillimetresToApi(display.OffsetXmm), DrawingCoordinates.MillimetresToApi(display.OffsetYmm));
            placement.Height = 2.5;
            placement.Justification = TextBase.JustificationType.BaseLineLeft;
            placement.IsVisible = true;
            using AnyPropertyId actual = placement.DisplayedProperty;
            Require(actual.AsInt == display.PropertyId && placement.Parent.ObjectIdentifier == function.ObjectIdentifier
                && placement.Location.X == display.OffsetXmm && placement.Location.Y == display.OffsetYmm
                && placement.Height == 2.5 && placement.IsVisible && placement.Justification == TextBase.JustificationType.BaseLineLeft);
            string text = placement.GetDisplayString();
            if (display.PropertyId is 20214 or 20213 or 20211 or 20215)
            {
#pragma warning disable CA2249 // net481 has no string.Contains overload taking StringComparison; Small tests link this adapter on net10.
                Require(text.IndexOf(DeviceSymbolDisplays.DeviceTag(args, tag), StringComparison.Ordinal) >= 0);
#pragma warning restore CA2249
                visible ??= text;
            }
        }

        // A13: finder cannot see uncommitted functions. Check the returned function and its pins directly.
        Pin[] pins = function.Pins;
        Require(_symbol.FunctionCategory is null || function.FunctionCategory.ToString() == _symbol.FunctionCategory);
        Require(function.IsPlaced && function.Page.Name == page.Name && DevicePlaceRequest.SameName(tag, function.Name) && function.ArticleReferences.Length == 0
            && function.Location.X == args.X && function.Location.Y == args.Y && pins.Length == _symbol.ConnectionPoints.Count
            && function.SymbolVariant.SymbolLibraryName == _symbol.Library && function.SymbolVariant.SymbolName == _symbol.Name
            && function.SymbolVariant.VariantNr == _symbol.Variant);
        for (int i = 0; i < (requested?.Length ?? 0); i++)
        {
            using PropertyValue value = properties[(Properties.Function)20022, i + 1];
            Require(!value.IsEmpty && value.ToString() == requested![i] && pins[i].Name == requested[i]);
        }
        for (int i = 0; i < (descriptions?.Length ?? 0); i++)
        {
            using PropertyValue value = properties[(Properties.Function)20023, i + 1];
            Require(!value.IsEmpty && value.ToString() == descriptions![i]);
        }
        PlcChannel? plc = args.PlcConnectionPoint is null ? null : EplanPlcReader.Channel((Terminal)function, new TextLanguage(ISOCode.Language.L___, false), properties);
        _placedFunctionId = function.ObjectIdentifier;
        _placedLayout = EplanLayout.Measure(_projectPath, _pageName, [function]);
        // A55: only managed identity, values and display snapshots may outlive this transaction.
        if (args.DisplayProperties is { Count: > 0 })
        { _placedDisplays = ReadDisplays(function); }
        return new DevicePlaceData { Page = page.Name, Tag = function.Name, VisibleTag = DeviceTags.Shown(visible), Category = function.FunctionCategory.ToString(), Functions = 1, Unplaced = 0, Plc = plc };
    }

    private static void Require(bool matches)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Symbol, tag, position, display or pin readback differs. Nothing was committed; read the page and re-plan."); }
    }
}
