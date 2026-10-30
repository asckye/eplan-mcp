using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    public IReadOnlyList<DeviceSymbolDisplay> ReadDisplays()
    {
        MainThread.Assert();
        try
        {
            Function function = _placedFunction!;
            // A55/A57: inspect only this placed point, never the extra device-level PLC terminal or cached Create handles.
            return [.. function.PropertyPlacements.Select(placement =>
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                using AnyPropertyId id = placement.DisplayedProperty;
                var parent = placement.Parent as Function;
                return new DeviceSymbolDisplay
                {
                    PropertyId = id.AsInt,
                    OnPlacedFunction = function.IsPlaced && parent?.ObjectIdentifier == function.ObjectIdentifier,
                    ParentTag = parent?.Name,
                    Page = parent?.Page?.Name,
                    Offset = [DrawingCoordinates.ApiToMillimetres(placement.Location.X), DrawingCoordinates.ApiToMillimetres(placement.Location.Y)],
                    Position = parent is null ? [] : [DrawingCoordinates.ApiToMillimetres(parent.Location.X + placement.Location.X), DrawingCoordinates.ApiToMillimetres(parent.Location.Y + placement.Location.Y)],
                    FormatMatches = placement.Height == 2.5 && placement.IsVisible && placement.Justification == TextBase.JustificationType.BaseLineLeft,
                    Text = placement.GetDisplayString()
                };
            })];
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException && new EplanExceptionMapper().Map(ex) is null)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN could not read the placed point's displays. Nothing was committed; read the page and re-plan.", ex);
        }
    }
}
