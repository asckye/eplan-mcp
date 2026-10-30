using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    private Function CreateFunction(DevicePlaceArgs args, string tag)
    {
#pragma warning disable CA2000 // A39/A55: persistent functions, PLCs and terminals belong to EPLAN.
        if (args.PlcConnectionPoint is not { } point)
        {
            Function function = _symbol.Kind == "plc_box" ? new PLC() : new Function();
            function.Create(_page, _variant);
            if (_symbol.Kind == "plc_box")
            { function.IsMainFunction = true; Require(function.IsMainFunction); }
            return function;
        }
        string parent = tag.Substring(0, tag.Length - point.Length - 1);
        using var finder = new DMObjectsFinder(_project);
        using var filter = new FunctionsFilter { Name = parent, ExactNameMatching = true, FunctionCategory = FunctionCategory.PLCBox };
        PLC[] cards = [.. finder.GetFunctions(filter).OfType<PLC>().Where(p => p.IsMainFunction && DevicePlaceRequest.SameName(parent, p.Name))];
        Require(cards.Length == 1);
        FunctionDefinition definition = _project.FunctionDefinitionLibrary.FunctionDefinitions.Single(d => d.FunctionCategory == FunctionCategory.PLCTerminal
            && FormattableString.Invariant($"{d.Group}/{d.Id}") == _symbol.PlcInputDefinition);
        Require(definition.ConnectionPoints.Length == _symbol.ConnectionPoints.Count);
        var terminal = new Terminal();
#pragma warning restore CA2000
        // Acceptance trial: Create(PLC, variant) instantiates a representation at the page origin and PlaceAt then adds a
        // second function (PlaceAt docs), so each point left a visible device-level terminal at (0,0). Create the point
        // on the page in one step; it joins the card by its device tag (Name is set by the caller).
        terminal.Create(_page, _variant);
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
        properties.FUNC_PLCADDRESS = args.PlcAddress!;
        properties.FUNC_PLCSYMBOLICADDRESS_MANUAL = args.PlcSymbolicAddress!;
        using var text = new MultiLangString();
        text.AddString(ISOCode.Language.L___, args.PlcText!);
        properties.FUNC_TEXT = text;
    }
}
