using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    public EplanDeviceSymbolPlacement(Project project, Page page) : this("project", page.Name, new() { Library = "SPECIAL", Name = "SC", Variant = 0 })
        => EplanProjects.Project = project;

    public DevicePlaceData PlaceBox(DevicePlaceArgs args) => CreateLocationBox(args, EplanProjects.Project!,
        EplanProjects.Project!.Pages.Single(p => p.Name == _pageName), new SymbolVariant());

    public DevicePlaceData PlaceTerminal(DevicePlaceArgs args, string tag)
    {
        _symbol.Library = "IEC_symbol";
        _symbol.Name = "X";
        return CreateTerminal(args, tag, EplanProjects.Project!, EplanProjects.Project!.Pages.Single(p => p.Name == _pageName), new SymbolVariant { SymbolName = "X" });
    }
}
