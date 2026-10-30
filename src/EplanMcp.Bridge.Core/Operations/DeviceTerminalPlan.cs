using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class DeviceTerminalPlan
{
    internal static void Check(DevicePlaceArgs args)
    {
        if (args.TerminalStripText is { } raw && TextLanguages.Stored(raw).Count > 0)
        { throw WriteArguments.Invalid(["/terminal_strip_text: use plain language-independent text (??_??), without serialized language entries; do not mix it with real languages (A54)."]); }
        if (args.TerminalStrip is null)
        {
            if (args.TerminalNumber is not null || args.TerminalStripText is not null)
            { throw WriteArguments.Invalid(["/terminal_strip: terminal_number and terminal_strip_text require a terminal source."]); }
            return;
        }
        if (args.Part is not null || args.Variant is not null || args.Library is not null || args.Symbol is not null || args.Tag is not null
            || args.LocationBox is not null || args.CornerX is not null || args.CornerY is not null || args.DisplayProperties is not null || args.ConnectionDesignations is not null
            || args.SymbolVariant is < 0)
        { throw WriteArguments.Invalid(["/terminal_strip: use an exclusive terminal source with optional symbol_variant; omit tag and other sources."]); }
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
        if (!create && args.TerminalStripText is not null)
        { throw WriteArguments.Invalid(["/terminal_strip_text: supply text only when creating a new strip; omit it when adding another terminal."]); }
        changes[0].Kind = "terminal";
        if (create)
        { changes.Insert(0, new() { Action = "create", Kind = "terminal_strip", Name = strip, NewValue = "main definition; function text (??_??): " + (args.TerminalStripText ?? "") }); }
        return new() { Strip = strip, Number = args.TerminalNumber!, Position = [args.X, args.Y], StripCreated = create };
    }
}
