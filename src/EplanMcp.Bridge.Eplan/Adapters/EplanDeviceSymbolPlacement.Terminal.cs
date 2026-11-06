using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    /// <summary>
    /// Finds the terminal's strip or creates it with its text and position, inside a Transaction (A13: Function.Create
    /// always fails in a SafetyPoint). A terminal placed from its part then joins the strip by name, as on an existing strip.
    /// </summary>
    internal static (TerminalStrip Strip, bool Created) PrepareStrip(DevicePlaceArgs args, string tag, Project project, Page page)
    {
        MainThread.Assert();
        string name = tag.Substring(0, tag.LastIndexOf(':'));
        using var finder = new DMObjectsFinder(project);
        TerminalStrip[] matches = [.. EplanNativePreconditions.Valid(finder.GetTerminalStrips(null)).ToArray().Where(s => Same(name.ToUpperInvariant(), s.Name.ToUpperInvariant()))];
        CheckTerminal(matches.Length <= 1, "several strips share this name");
        bool create = matches.Length == 0;
        CheckTerminal(!create || !EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray().Any(f => Same(name.ToUpperInvariant(), f.Name.ToUpperInvariant())), "another function already has the strip name");
#pragma warning disable CA2000 // A39/A55: persistent strips and terminals belong to EPLAN, also after Abort.
        TerminalStrip strip = create ? new TerminalStrip() : matches[0];
        if (create)
        {
            // Acceptance trial: Create(Page) placed the strip definition at the page origin, outside the frame. An
            // unplaced definition (Create(Project, FunctionDefinition)) is listed in the navigator only.
            if (args.TerminalStripPosition is { } position)
            {
                // A83: the insert command uses a SPECIAL symbol; omitted plugin selection retains TDEF/0.
                StripDefinitionSymbol selection = args.StripDefinition ?? new();
                CheckTerminal(selection.Library == "SPECIAL", "plugin strip definitions only support the SPECIAL library");
                SymbolVariant? definition = project.SymbolLibraries.FirstOrDefault(l => l.Name == "SPECIAL")?.Symbols.FirstOrDefault(s => s.Name == selection.Symbol)?.Variants.FirstOrDefault(v => v.VariantNr == selection.Variant);
                CheckTerminal(definition is not null, $"the SPECIAL/{selection.Symbol}/{selection.Variant} strip definition symbol is not loaded");
                strip.Create(page, definition!);
                CheckTerminal(strip.SymbolVariant.SymbolLibraryName == "SPECIAL" && strip.SymbolVariant.SymbolName == selection.Symbol
                    && strip.SymbolVariant.VariantNr == selection.Variant, "strip definition symbol differs");
                strip.Location = new PointD(position[0], position[1]);
            }
            else
            { strip.Create(project, project.FunctionDefinitionLibrary.FunctionDefinitions.First(d => d.FunctionCategory == FunctionCategory.TerminalDefText)); }
            strip.IsMainFunction = true;
            strip.Name = name;
        }
#pragma warning restore CA2000
        if (args.TerminalStripText is { } text)
        {
            CheckTerminal(create, "strip text is only set on a new strip");
            using TerminalStripPropertyListComplete properties = strip.Properties;
            using var multilingual = new MultiLangString();
            if (TextLanguages.IsSerialized(text))
            { multilingual.SetAsString(text); }
            else
            { multilingual.AddString(ISOCode.Language.L___, text); }
            properties.FUNC_TEXT = multilingual;
            using PropertyValue value = properties.FUNC_TEXT;
            CheckTerminal(!value.IsEmpty, "strip text is empty after writing");
            using MultiLangString actual = value.ToMultiLangString();
            string read = TextLanguages.IsSerialized(text) ? actual.GetAsString() : actual.GetStringToDisplay(ISOCode.Language.L___);
            CheckTerminal(TextLanguages.ReadsBackAs(text, read), "strip text reads back as '" + read + "'");
        }
        // Customer redraw 2026-11-02: the strip definition keeps its symbol's default property arrangement ("-X6 =
        // text"); it only lacked a visible name. Creating or undocking texts switched it to a user-defined arrangement.
        if (args.TerminalStripPosition is { } expected)
        {
            CheckTerminal(create && strip.IsPlaced && strip.Page.Name == page.Name && strip.Location.X == expected[0] && strip.Location.Y == expected[1], "strip definition page or position differs");
            EplanVisibleNames.Adjust(page, strip);
        }
        return (strip, create);
    }

    private DevicePlaceData CreateTerminal(DevicePlaceArgs args, string tag, Project project, Page page, SymbolVariant variant)
    {
        (TerminalStrip strip, bool create) = PrepareStrip(args, tag, project, page);
        return CreateTerminal(args, tag, project, page, variant, strip, create);
    }

    internal DevicePlaceData CreateTerminal(DevicePlaceArgs args, string tag, Project project, Page page, SymbolVariant variant, TerminalStrip strip, bool create, Terminal? placed = null)
    {
        MainThread.Assert();
        string name = tag.Substring(0, tag.LastIndexOf(':'));
#pragma warning disable CA2000 // A39/A55: persistent terminals belong to EPLAN, also after Abort.
        var terminal = placed ?? new Terminal();
#pragma warning restore CA2000
        CheckTerminal(!strip.Terminals.Any(t => Same(tag.ToUpperInvariant(), t.Name.ToUpperInvariant())), "the strip already has this terminal");
        if (placed is null && _symbol.FunctionCategory is { } category && category != "Terminal")
        {
            SymbolVariant plain = project.SymbolLibraries.Single(l => l.Name == "IEC_symbol").Symbols.Single(s => s.Name == "X2_2").Variants.Single(v => v.VariantNr == 0);
            terminal.Create(strip, page, plain);
            terminal.SetSymbolVariant(variant, false);
        }
        else if (placed is null)
        { terminal.Create(strip, page, variant); }
        terminal.Name = tag;
        terminal.Location = new PointD(args.X, args.Y);
        // PDF redraw 2026-11-06: CreateDevice leaves its own visible name, which shows the strip on every terminal.
        if (placed is not null)
        { terminal.VisibleName = string.Empty; }
        EplanVisibleNames.Adjust(page, terminal);
        if (args.PropertyArrangement is { } arrangement)
        { EplanSymbolArrangement.Select(terminal, arrangement); }
        // A55: release the cached list before TransactionRunner can Abort; never access these objects afterwards.
        using FunctionPropertyList values = terminal.Properties;
        values.FUNC_PINORTERMINALNUMBER = args.TerminalNumber!;
        using PropertyValue number = values.FUNC_PINORTERMINALNUMBER;
        string? sortCode = null;
        try
        {
            using PropertyValue sort = values.FUNC_TERMINALSORTCODE;
            sortCode = sort.IsEmpty ? null : sort.ToString();
        }
        catch (PropertyNotFoundException)
        {
            // Live 2026-11-03: GRAPHICS/KA1_PE terminals have no sort code property (S063109); the result only reports it.
        }
        TerminalStrip? owner = terminal.TerminalStrip;
        CheckTerminal(Same(name, strip.Name) && strip.IsMainFunction, $"strip reads back as '{strip.Name}', main function {strip.IsMainFunction}");
        CheckTerminal(Same(tag, terminal.Name), $"terminal name reads back as '{terminal.Name}'");
        CheckTerminal(!number.IsEmpty && number.ToString() == args.TerminalNumber, $"terminal number reads back as '{(number.IsEmpty ? "" : number.ToString())}'");
        // S43/live T7c: the strip relation can stay null until the commit; eplan_terminal_strip_get confirms it afterwards.
        // Live T7c: an existing strip's owner object differs from the finder's strip (other OID, same name); strips are identified by name.
        CheckTerminal(owner is null || Same(name, owner.Name), $"terminal owner reads back as '{owner?.Name}'");
        CheckTerminal(terminal.FunctionCategory.ToString() == "Terminal", $"category reads back as {terminal.FunctionCategory}");
        CheckTerminal(terminal.IsPlaced && terminal.Page.Name == page.Name, "terminal is not placed on the page");
        CheckTerminal(terminal.Location.X == args.X && terminal.Location.Y == args.Y, FormattableString.Invariant($"position reads back as {terminal.Location.X},{terminal.Location.Y}"));
        CheckTerminal(args.Part is not null || terminal.ArticleReferences.Length == 0, "terminal has parts");
        CheckTerminal(terminal.SymbolVariant.SymbolLibraryName == _symbol.Library && terminal.SymbolVariant.SymbolName == _symbol.Name && terminal.SymbolVariant.VariantNr == _symbol.Variant,
            $"symbol reads back as {terminal.SymbolVariant.SymbolLibraryName}/{terminal.SymbolVariant.SymbolName}/{terminal.SymbolVariant.VariantNr}");
        OperationContext.Current.ThrowIfCancellationRequested();
        _placedLayout = EplanLayout.Measure(_projectPath, _pageName, [terminal]);
        return new()
        {
            PropertyArrangement = args.PropertyArrangement is null ? null : EplanSymbolArrangement.SelectedName(terminal),
            ActivePropertySet = args.PropertyArrangement is null ? null : EplanSymbolArrangement.ReadActive(values),
            Page = page.Name,
            Tag = terminal.Name,
            Category = "Terminal",
            Functions = create ? 2 : 1,
            Terminal = new()
            {
                Strip = strip.Name,
                Number = number.ToString(),
                Owner = owner?.Name,
                Position = [terminal.Location.X, terminal.Location.Y],
                SortCode = sortCode,
                StripCreated = create,
                StripPosition = args.TerminalStripPosition is null ? null : [strip.Location.X, strip.Location.Y],
                StripText = args.TerminalStripText
            }
        };
    }

    /// <summary>
    /// Live T7c: the sample project's structure settings leave the higher-level function out of terminal strip and
    /// terminal names (=GAA+A1-XT4 reads back as +A1-XT4). Only that leading segment may be missing.
    /// </summary>
    private static bool Same(string requested, string actual) => DevicePlaceRequest.SameName(requested, actual);

    private static void CheckTerminal(bool matches, string what)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Terminal readback differs (" + what + "). Nothing was committed; read the page and re-plan."); }
    }
}
