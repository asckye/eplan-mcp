using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement(Project project, Page page)
{
    private readonly Project _project = project;
    private readonly Page _page = page;
    private readonly object _variant = new();
    private readonly SymbolSearchItem _symbol = new() { Library = "SPECIAL", Name = "SC", Variant = 0 };
    public DevicePlaceData PlaceBox(DevicePlaceArgs args) => CreateLocationBox(args);
    private static void Require(bool matches)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Readback differs."); }
    }
}
