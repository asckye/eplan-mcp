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
        Require(cards.Length == 1);
        _plcCardId = cards[0].ObjectIdentifier;
        // PDF redraw 2026-11-06: EPLAN pairs a point with its overview point by card, function definition, plug and designation, so an
        // output channel takes the definition of its overview point; a point without one stays the symbol's digital input.
        using var twins = new FunctionsFilter { Name = tag, ExactNameMatching = true, FunctionCategory = FunctionCategory.PLCTerminal };
        FunctionDefinition definition = EplanNativePreconditions.Valid(finder.GetFunctions(twins)).FirstOrDefault(f => f.FunctionCategory == FunctionCategory.PLCTerminal && f.IsPlaced && f.Page.PageType == DocumentTypeManager.DocumentType.Overview)?.FunctionDefinition
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
