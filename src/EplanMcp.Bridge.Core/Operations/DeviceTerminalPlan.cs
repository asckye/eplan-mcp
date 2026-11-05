using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class DeviceTerminalPlan
{
    internal static void Check(DevicePlaceArgs args)
    {
        if (args.StripDefinition is { } definition && (definition.Library != "SPECIAL" || string.IsNullOrWhiteSpace(definition.Symbol)
            || definition.Symbol.Any(c => char.IsControl(c) || c is '/' or '\\' or ':') || definition.Variant < 0))
        { throw WriteArguments.Invalid(["/stripDefinition: plugin strip definitions require a SPECIAL symbol name and a nonnegative variant; other libraries are not supported."]); }
        if (args.TerminalStripPosition is { } position && (args.TerminalStrip is null || args.TerminalStripText is null || position.Length != 2
            || position.Any(n => double.IsNaN(n) || double.IsInfinity(n) || n is < 0 or > 10000)))
        { throw WriteArguments.Invalid(["/terminal_strip_position: require a terminal source, new strip text and two finite page coordinates 0..10000 mm."]); }
        if (args.TerminalStripText is { } raw && TextLanguages.Stored(raw).Count > 0)
        { throw WriteArguments.Invalid(["/terminal_strip_text: use plain language-independent text (??_??), without serialized language entries; do not mix it with real languages (A54)."]); }
        if (args.TerminalStrip is null)
        {
            if (args.TerminalNumber is not null || args.TerminalStripText is not null)
            { throw WriteArguments.Invalid(["/terminal_strip: terminal_number and terminal_strip_text require a terminal source."]); }
            return;
        }
        if (args.Variant is not null && args.Part is null || args.Tag is not null
            || args.LocationBox is not null || args.CornerX is not null || args.CornerY is not null || args.DisplayProperties is not null || args.ConnectionDesignations is not null || args.ConnectionDescriptions is not null
            || args.SymbolVariant is < 0)
        { throw WriteArguments.Invalid(["/terminal_strip: use an exclusive terminal source with optional library/symbol and symbol_variant; omit tag and other sources."]); }
        if ((args.Library is not null || args.Symbol is not null) && (string.IsNullOrWhiteSpace(args.Library) || string.IsNullOrWhiteSpace(args.Symbol)
            || args.Library!.Any(char.IsControl) || args.Symbol!.Any(char.IsControl) || args.Part is not null && args.SymbolVariant is null))
        { throw WriteArguments.Invalid(["/library: terminal symbols require a complete library/symbol pair without controls."]); }
        if (args.TerminalStrip.Length is < 2 or > 128 || args.TerminalStrip.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == ':')
            || args.TerminalStrip.LastIndexOf('-') < 0 || args.TerminalStrip[args.TerminalStrip.Length - 1] == '-')
        { throw WriteArguments.Invalid(["/terminal_strip: require a short or full device tag, at most 128 characters, without whitespace, controls or colon."]); }
        if (args.TerminalNumber is not { Length: > 0 and <= 64 } number || number.Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.')))
        { throw WriteArguments.Invalid(["/terminal_number: require 1-64 letters, digits, '_' or '.'."]); }
        if (args.TerminalStripText is { } text && (string.IsNullOrWhiteSpace(text) || text.Length > 256 || text.Any(char.IsControl)))
        { throw WriteArguments.Invalid(["/terminal_strip_text: require nonempty text of at most 256 characters without controls."]); }
    }

    internal static TerminalPlacementData Describe(DevicePlaceArgs args, DevicePlaceRequest request, List<PlannedChange> changes)
    {
        string strip = request.Tag.Substring(0, request.Tag.LastIndexOf(':'));
        bool create = !request.Existing.Contains(strip, StringComparer.OrdinalIgnoreCase);
        if (!create && (args.TerminalStripText is not null || args.TerminalStripPosition is not null))
        { throw WriteArguments.Invalid(["/terminal_strip_text: supply text only when creating a new strip; omit it when adding another terminal."]); }
        changes[0].Kind = "terminal";
        if (create)
        {
            string symbol = args.StripDefinition is { } definition ? FormattableString.Invariant($"; SPECIAL/{definition.Symbol}/{definition.Variant}") : "";
            changes.Insert(0, new() { Action = "create", Kind = "terminal_strip", Name = strip, NewValue = "main definition; function text (??_??): " + (args.TerminalStripText ?? "") + (args.TerminalStripPosition is { } point ? FormattableString.Invariant($"; page {request.Page.Name} at ({point[0]}, {point[1]})") + symbol : "; unplaced") });
        }
        if (args.TerminalStripPosition is { } xy && (request.Page.Width is not { } w || request.Page.Height is not { } h || double.IsNaN(w) || double.IsInfinity(w) || double.IsNaN(h) || double.IsInfinity(h) || w <= 0 || h <= 0 || xy[0] > w || xy[1] > h))
        { throw WriteArguments.Invalid(["/terminal_strip_position: require known page bounds and a position inside the page."]); }
        return new() { Strip = strip, Number = args.TerminalNumber!, Position = [args.X, args.Y], StripCreated = create, StripPosition = args.TerminalStripPosition, StripText = args.TerminalStripText };
    }
}
