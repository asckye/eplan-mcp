using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    private Function CreateFunction(DevicePlaceArgs args, string tag, Project project, Page page, SymbolVariant variant)
    {
#pragma warning disable CA2000 // A39/A55: persistent functions, PLCs and terminals belong to EPLAN.
        if (args.PlcConnectionPoint is null)
        {
            Function function = _symbol.Kind == "plc_box" ? new PLC() : new Function();
            function.Create(page, variant);
            if (_symbol.Kind == "plc_box")
            { function.IsMainFunction = true; Require(function.IsMainFunction); }
            return function;
        }
        string parent = DevicePlcPlan.Parent(args, tag);
        using var finder = new DMObjectsFinder(project);
        using var filter = new FunctionsFilter { Name = parent, ExactNameMatching = true, FunctionCategory = FunctionCategory.PLCBox };
        PLC[] cards = [.. EplanNativePreconditions.Valid(finder.GetFunctions(filter)).ToArray().OfType<PLC>().Where(p => p.IsMainFunction && DevicePlaceRequest.SameName(parent, p.Name))];
        // A170: the filter by name and category misses functions the unfiltered read omits; only then the complete read.
        if (cards.Length == 0)
        { cards = [.. EplanDeviceReader.NamedFunctions(finder, parent).OfType<PLC>().Where(p => p.IsMainFunction && p.FunctionCategory == FunctionCategory.PLCBox)]; }
        Require(cards.Length == 1);
        _plcCardId = cards[0].ObjectIdentifier;
        // PDF redraw 2026-11-06: EPLAN pairs a point with its overview point by card, function definition, plug and designation, so an
        // output channel takes the definition of its overview point; a point without one stays the symbol's digital input.
        using var twins = new FunctionsFilter { Name = tag, ExactNameMatching = true, FunctionCategory = FunctionCategory.PLCTerminal };
        // A170: a twin the filter misses would leave the new point with the symbol's default input definition.
        FunctionDefinition definition = (EplanNativePreconditions.Valid(finder.GetFunctions(twins)).FirstOrDefault(f => f.FunctionCategory == FunctionCategory.PLCTerminal && f.IsPlaced && f.Page.PageType == DocumentTypeManager.DocumentType.Overview) ?? OverviewTwin(project, finder, tag))?.FunctionDefinition
            ?? project.FunctionDefinitionLibrary.FunctionDefinitions.Single(d => d.FunctionCategory == FunctionCategory.PLCTerminal
                && FormattableString.Invariant($"{d.Group}/{d.Id}") == _symbol.PlcInputDefinition);
        Require(definition.ConnectionPoints.Length == _symbol.ConnectionPoints.Count);
        var terminal = new Terminal();
#pragma warning restore CA2000
        // Acceptance trial: Create(PLC, variant) instantiates a representation at the page origin and PlaceAt then adds a
        // second function (PlaceAt docs), so each point left a visible device-level terminal at (0,0). Create the point
        // on the page in one step; it joins the card by its device tag (Name is set by the caller).
        terminal.Create(page, variant);
        terminal.FunctionDefinition = definition;
        Require(terminal.FunctionCategory == FunctionCategory.PLCTerminal && terminal.FunctionDefinition.Group == definition.Group && terminal.FunctionDefinition.Id == definition.Id);
        // A55/live T7c: relations may be null before commit; compare names rather than object identifiers.
        Require(terminal.PLC is null || DevicePlaceRequest.SameName(parent, terminal.PLC.Name));
        return terminal;
    }

    /// <summary>
    /// The point's overview representation, read page by page on the overview pages only. The filter by name and
    /// category misses functions the unfiltered read omits (A170); a read by page does not, and this path must not
    /// scan every function of the project inside its transaction.
    /// </summary>
    private static Function? OverviewTwin(Project project, DMObjectsFinder finder, string tag)
    {
        foreach (Page overview in EplanNativePreconditions.Valid(project.Pages).ToArray().Where(p => p.PageType == DocumentTypeManager.DocumentType.Overview))
        {
            using var onPage = new FunctionsFilter { Page = overview };
            if (EplanNativePreconditions.Valid(finder.GetFunctions(onPage)).FirstOrDefault(f => f.FunctionCategory == FunctionCategory.PLCTerminal && f.IsPlaced && f.Name == tag) is { } twin)
            { return twin; }
        }
        return null;
    }

    private static void WritePlcPoint(FunctionPropertyList properties, DevicePlaceArgs args)
    {
        if (args.PlcConnectionPoint is null)
        { return; }
        if (args.PlcPlugDesignation is { } plug)
        { properties.FUNC_PLCPLUG_DESIGNATION_MANUAL = plug; }
        if (args.DisplayProperties?.Any(p => p.PropertyId is 20300 or 20301) == true)
        {
            properties.FUNC_SUPPRESSCROSSREFERENCE = 2;
            using PropertyValue visible = properties.FUNC_SUPPRESSCROSSREFERENCE;
            Require(!visible.IsEmpty && visible.ToInt() == 2);
        }
        if (args.PlcAddress is { } address)
        { properties.FUNC_PLCADDRESS = address; }
        if (args.PlcSymbolicAddress is { } symbolic)
        { properties.FUNC_PLCSYMBOLICADDRESS_MANUAL = symbolic; }
        if (args.PlcText is { } value)
        {
            using var text = new MultiLangString();
            if (TextLanguages.IsSerialized(value))
            { text.SetAsString(value); }
            else
            { text.AddString(ISOCode.Language.L___, value); }
            properties.FUNC_TEXT = text;
        }
    }
}
