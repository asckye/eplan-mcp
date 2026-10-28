using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    private DevicePlaceData CreateTerminal(DevicePlaceArgs args, string tag)
    {
        MainThread.Assert();
        string name = tag.Substring(0, tag.LastIndexOf(':'));
        using var finder = new DMObjectsFinder(_project);
        TerminalStrip[] matches = [.. finder.GetTerminalStrips(null).Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))];
        Require(matches.Length <= 1);
        bool create = matches.Length == 0;
        Require(!create || !finder.GetFunctions(null).Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)));
#pragma warning disable CA2000 // A39/A55: persistent strips and terminals belong to EPLAN, also after Abort.
        TerminalStrip strip = create ? new TerminalStrip() : matches[0];
        if (create)
        {
            strip.Create(_page);
            strip.IsMainFunction = true;
            strip.Name = name;
        }
        if (args.TerminalStripText is { } text)
        {
            Require(create);
            using TerminalStripPropertyListComplete properties = strip.Properties;
            using var multilingual = new MultiLangString();
            multilingual.AddString(ISOCode.Language.L___, text);
            properties.FUNC_TEXT = multilingual;
            using PropertyValue value = properties.FUNC_TEXT;
            Require(!value.IsEmpty);
            using MultiLangString actual = value.ToMultiLangString();
            Require(actual.GetStringToDisplay(ISOCode.Language.L___) == text);
        }
        var terminal = new Terminal();
#pragma warning restore CA2000
        Require(!strip.Terminals.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase)));
        terminal.Create(strip, _page, _variant);
        terminal.Name = tag;
        terminal.Location = new PointD(args.X, args.Y);
        // A55: release the cached list before TransactionRunner can Abort; never access these objects afterwards.
        using FunctionPropertyList values = terminal.Properties;
        values.FUNC_PINORTERMINALNUMBER = args.TerminalNumber!;
        using PropertyValue number = values.FUNC_PINORTERMINALNUMBER;
        using PropertyValue sort = values.FUNC_TERMINALSORTCODE;
        TerminalStrip? owner = terminal.TerminalStrip;
        Require(strip.Name == name && strip.IsMainFunction && terminal.Name == tag && !number.IsEmpty && number.ToString() == args.TerminalNumber
            && owner is not null && owner.ObjectIdentifier == strip.ObjectIdentifier && owner.Name == name
            && terminal.FunctionCategory.ToString() == "Terminal" && terminal.IsPlaced && terminal.Page.Name == _page.Name
            && terminal.Location.X == args.X && terminal.Location.Y == args.Y && terminal.ArticleReferences.Length == 0
            && terminal.SymbolVariant.SymbolLibraryName == _symbol.Library && terminal.SymbolVariant.SymbolName == _symbol.Name && terminal.SymbolVariant.VariantNr == _symbol.Variant);
        OperationContext.Current.ThrowIfCancellationRequested();
        return new()
        {
            Page = _page.Name,
            Tag = terminal.Name,
            Category = "Terminal",
            Functions = create ? 2 : 1,
            Terminal = new()
            {
                Strip = strip.Name,
                Number = number.ToString(),
                Owner = owner!.Name,
                Position = [terminal.Location.X, terminal.Location.Y],
                SortCode = sort.IsEmpty ? null : sort.ToString(),
                StripCreated = create
            }
        };
    }
}
