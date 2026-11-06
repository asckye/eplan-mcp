using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    public void AttachPart(DevicePlaceArgs args, DevicePlaceData data)
    {
        MainThread.Assert();
        Function Placed() => EplanNativePreconditions.Valid(EplanProjects.ByPath(_projectPath).Pages
            .Single(p => p.Name == _pageName).AllPlacements).OfType<Function>().Single(f => f.ObjectIdentifier == _placedFunctionId);
        string variant = args.Variant ?? DevicePlaceRequest.DefaultVariant;
        // A95: append only after creation commits; each transaction reacquires the function.
        using (Transaction transaction = new TransactionManager().CreateTransaction())
        {
            Function placed = Placed();
            placed.AddArticleReference(args.Part!, variant, 1);
            transaction.Commit();
            GC.KeepAlive(placed);
        }
        Function committed = Placed();
        if (!committed.ArticleReferences.Any(a => a.PartNr == args.Part && a.VariantNr == variant && a.Count == 1))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not keep the requested part on the placed symbol."); }
        GC.KeepAlive(committed);
        data.Part = args.Part!;
        data.Variant = variant;
    }

    public void ReadCommitted(DevicePlaceArgs args, DevicePlaceData data)
    {
        MainThread.Assert();
        if (args.PlcConnectionPoint is null)
        { ReadSpecialCommitted(args, data); return; }
        string verification = "the placed PLC point's identity and page";
        try
        {
            // A55: reacquire every native handle after Commit/Dispose; D3 names can differ from the request.
            Project project = EplanProjects.ByPath(_projectPath);
            Page page = project.Pages.Single(p => string.Equals(p.Name, _pageName, StringComparison.Ordinal));
            Terminal? terminal = EplanNativePreconditions.Valid(page.AllPlacements).ToArray().OfType<Terminal>()
                .SingleOrDefault(f => f.ObjectIdentifier == _placedFunctionId);
            Require(terminal is not null && terminal.IsPlaced && !terminal.IsMainFunction && terminal.Page.Name == data.Page);
            data.Tag = terminal!.Name;

            verification = "PLC card ownership";
            using var finder = new DMObjectsFinder(project);
            using var filter = new FunctionsFilter { FunctionCategory = FunctionCategory.PLCBox };
            PLC? card = EplanNativePreconditions.Valid(finder.GetFunctions(filter)).ToArray().OfType<PLC>()
                .SingleOrDefault(c => c.ObjectIdentifier == _plcCardId);
            Require(card is not null && card.IsMainFunction
                && card.PLCTerminals.Any(c => c.ObjectIdentifier == terminal.ObjectIdentifier)
                && (terminal.PLC is null || terminal.PLC.ObjectIdentifier == card.ObjectIdentifier));

            verification = "PLC address, manual symbolic address, function text, plug designation (20406) and native references (20300/20301)";
            PlcChannel channel;
            using (FunctionPropertyList properties = terminal.Properties)
            { channel = EplanPlcReader.Channel(terminal, new TextLanguage(ISOCode.Language.L___, false), properties); }
            // A55: Terminal.PLC can stay null; membership in the freshly acquired card establishes ownership.
            channel.Owner = card!.Name;
            Require((args.PlcAddress is null || channel.Address == args.PlcAddress)
                && (args.PlcSymbolicAddress is null || channel.ManualSymbolicAddress == args.PlcSymbolicAddress)
                && (args.PlcText is null || TextLanguages.ReadsBackAs(args.PlcText, channel.Text))
                && (args.PlcPlugDesignation is null || channel.PlugDesignation == args.PlcPlugDesignation));
            data.Plc = channel;

            verification = "the requested PLC displays (property, parent, page, position, format and native text, including plug designation and main reference)";
            if (args.DisplayProperties is { Count: > 0 })
            { DeviceSymbolDisplays.Verify(args, terminal.Name, data.Page, channel, ReadDisplays(terminal)); }
        }
        catch (Exception failure)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                $"The PLC point was committed, but {verification} could not be verified. Inspect the page and eplan_plc_get before repeating the placement.", failure)
            { Error = { Execution = ExecutionState.Committed } };
        }
    }

    public IReadOnlyList<DeviceSymbolDisplay> ReadDisplays() => _placedDisplays;

    private static IReadOnlyList<DeviceSymbolDisplay> ReadDisplays(Function function)
    {
        MainThread.Assert();
        try
        {
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
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "EPLAN could not read the placed point's displays. Nothing was committed; read the page and re-plan.", ex);
        }
    }
}
