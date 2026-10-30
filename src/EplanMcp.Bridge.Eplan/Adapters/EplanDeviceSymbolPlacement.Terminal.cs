using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core;
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
        TerminalStrip[] matches = [.. finder.GetTerminalStrips(null).Where(s => Same(name.ToUpperInvariant(), s.Name.ToUpperInvariant()))];
        CheckTerminal(matches.Length <= 1, "several strips share this name");
        bool create = matches.Length == 0;
        CheckTerminal(!create || !finder.GetFunctions(null).Any(f => Same(name.ToUpperInvariant(), f.Name.ToUpperInvariant())), "another function already has the strip name");
#pragma warning disable CA2000 // A39/A55: persistent strips and terminals belong to EPLAN, also after Abort.
        TerminalStrip strip = create ? new TerminalStrip() : matches[0];
        if (create)
        {
            // Acceptance trial: Create(Page) placed the strip definition at the page origin, outside the frame. An
            // unplaced definition (Create(Project, FunctionDefinition)) is listed in the navigator only.
            strip.Create(_project, _project.FunctionDefinitionLibrary.FunctionDefinitions.First(d => d.FunctionCategory == FunctionCategory.TerminalDefText));
            strip.IsMainFunction = true;
            strip.Name = name;
        }
        if (args.TerminalStripText is { } text)
        {
            CheckTerminal(create, "strip text is only set on a new strip");
            using TerminalStripPropertyListComplete properties = strip.Properties;
            using var multilingual = new MultiLangString();
            multilingual.AddString(ISOCode.Language.L___, text);
            properties.FUNC_TEXT = multilingual;
            using PropertyValue value = properties.FUNC_TEXT;
            CheckTerminal(!value.IsEmpty, "strip text is empty after writing");
            using MultiLangString actual = value.ToMultiLangString();
            CheckTerminal(actual.GetStringToDisplay(ISOCode.Language.L___) == text, "strip text reads back as '" + actual.GetStringToDisplay(ISOCode.Language.L___) + "'");
        }
        var terminal = new Terminal();
#pragma warning restore CA2000
        CheckTerminal(!strip.Terminals.Any(t => Same(tag.ToUpperInvariant(), t.Name.ToUpperInvariant())), "the strip already has this terminal");
        terminal.Create(strip, _page, _variant);
        terminal.Name = tag;
        terminal.Location = new PointD(args.X, args.Y);
        // A55: release the cached list before TransactionRunner can Abort; never access these objects afterwards.
        using FunctionPropertyList values = terminal.Properties;
        values.FUNC_PINORTERMINALNUMBER = args.TerminalNumber!;
        using PropertyValue number = values.FUNC_PINORTERMINALNUMBER;
        using PropertyValue sort = values.FUNC_TERMINALSORTCODE;
        TerminalStrip? owner = terminal.TerminalStrip;
        CheckTerminal(Same(name, strip.Name) && strip.IsMainFunction, $"strip reads back as '{strip.Name}', main function {strip.IsMainFunction}");
        CheckTerminal(Same(tag, terminal.Name), $"terminal name reads back as '{terminal.Name}'");
        CheckTerminal(!number.IsEmpty && number.ToString() == args.TerminalNumber, $"terminal number reads back as '{(number.IsEmpty ? "" : number.ToString())}'");
        // S43/live T7c: the strip relation can stay null until the commit; eplan_terminal_strip_get confirms it afterwards.
        // Live T7c: an existing strip's owner object differs from the finder's strip (other OID, same name); strips are identified by name.
        CheckTerminal(owner is null || Same(name, owner.Name), $"terminal owner reads back as '{owner?.Name}'");
        CheckTerminal(terminal.FunctionCategory.ToString() == "Terminal", $"category reads back as {terminal.FunctionCategory}");
        CheckTerminal(terminal.IsPlaced && terminal.Page.Name == _page.Name, "terminal is not placed on the page");
        CheckTerminal(terminal.Location.X == args.X && terminal.Location.Y == args.Y, FormattableString.Invariant($"position reads back as {terminal.Location.X},{terminal.Location.Y}"));
        CheckTerminal(terminal.ArticleReferences.Length == 0, "terminal has parts");
        CheckTerminal(terminal.SymbolVariant.SymbolLibraryName == _symbol.Library && terminal.SymbolVariant.SymbolName == _symbol.Name && terminal.SymbolVariant.VariantNr == _symbol.Variant,
            $"symbol reads back as {terminal.SymbolVariant.SymbolLibraryName}/{terminal.SymbolVariant.SymbolName}/{terminal.SymbolVariant.VariantNr}");
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
                Owner = owner?.Name,
                Position = [terminal.Location.X, terminal.Location.Y],
                SortCode = sort.IsEmpty ? null : sort.ToString(),
                StripCreated = create
            }
        };
    }

    /// <summary>
    /// Live T7c: the sample project's structure settings leave the higher-level function out of terminal strip and
    /// terminal names (=GAA+A1-XT4 reads back as +A1-XT4). Only that leading segment may be missing.
    /// </summary>
    private static bool Same(string requested, string actual) =>
        requested == actual || (requested.Length > 0 && requested[0] == '=' && requested.EndsWith(actual, StringComparison.Ordinal)
            && actual.Length > 0 && actual[0] is '+' or '-' && requested.Substring(0, requested.Length - actual.Length).IndexOfAny(['+', '-', ':', '&']) < 0);

    private static void CheckTerminal(bool matches, string what)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Terminal readback differs (" + what + "). Nothing was committed; read the page and re-plan."); }
    }
}
