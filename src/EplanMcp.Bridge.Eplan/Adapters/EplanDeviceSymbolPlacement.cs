using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A13/A52-A55: resolve before writing, read returned objects inside Transaction; never dispose owned objects.</summary>
internal sealed partial class EplanDeviceSymbolPlacement : IDeviceSymbolPlacement
{
    private readonly Project _project;
    private readonly Page _page;
    private readonly SymbolVariant _variant;
    private readonly SymbolSearchItem _symbol;

    public EplanDeviceSymbolPlacement(string path, string page, SymbolSearchItem symbol)
    {
        MainThread.Assert();
        _project = EplanProjects.ByPath(path);
        _page = _project.Pages.Single(p => string.Equals(p.Name, page, StringComparison.Ordinal));
        _variant = _project.SymbolLibraries.Single(l => l.Name == symbol.Library).Symbols.Single(s => s.Name == symbol.Name)
            .Variants.Single(v => v.VariantNr == symbol.Variant);
        _symbol = symbol;
    }

    public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(_project, undoDescription);

    public DevicePlaceData Place(DevicePlaceArgs args, string tag)
    {
        try
        { return args.TerminalStrip is not null ? CreateTerminal(args, tag) : args.LocationBox is null ? Create(args, tag) : CreateLocationBox(args); }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN could not create the requested symbol or displays. Nothing was committed; read the page and re-plan.", ex);
        }
    }

    private DevicePlaceData Create(DevicePlaceArgs args, string tag)
    {
        MainThread.Assert();
#pragma warning disable CA2000 // New persistent objects belong to EPLAN, including on Abort (A39/A55).
        var function = new Function();
#pragma warning restore CA2000
        function.Create(_page, _variant);
        function.Name = tag;
        function.Location = new PointD(DrawingCoordinates.MillimetresToApi(args.X), DrawingCoordinates.MillimetresToApi(args.Y));
        // A55: the only cached list is released while unwinding Place, BEFORE TransactionRunner can Abort.
        using FunctionPropertyList properties = function.Properties;
        string[]? requested = args.ConnectionDesignations;
        for (int i = 0; i < (requested?.Length ?? 0); i++)
        { properties[(Properties.Function)20022, i + 1] = requested![i]; }
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
                Require(text.IndexOf(tag.Substring(tag.LastIndexOf('-')), StringComparison.Ordinal) >= 0);
                visible ??= text;
            }
        }

        // A13: finder cannot see uncommitted functions. Check the returned function and its pins directly.
        Pin[] pins = function.Pins;
        Require(function.IsPlaced && function.Page.Name == _page.Name && function.Name == tag && function.ArticleReferences.Length == 0
            && function.Location.X == args.X && function.Location.Y == args.Y && pins.Length == _symbol.ConnectionPoints.Count
            && function.SymbolVariant.SymbolLibraryName == _symbol.Library && function.SymbolVariant.SymbolName == _symbol.Name
            && function.SymbolVariant.VariantNr == _symbol.Variant);
        for (int i = 0; i < (requested?.Length ?? 0); i++)
        {
            using PropertyValue value = properties[(Properties.Function)20022, i + 1];
            Require(!value.IsEmpty && value.ToString() == requested![i] && pins[i].Name == requested[i]);
        }
        return new DevicePlaceData { Page = _page.Name, Tag = function.Name, VisibleTag = DeviceTags.Shown(visible), Category = function.FunctionCategory.ToString(), Functions = 1, Unplaced = 0 };
    }

    private static void Require(bool matches)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Symbol, tag, position, display or pin readback differs. Nothing was committed; read the page and re-plan."); }
    }
}
