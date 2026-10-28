using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>T1 discovery before writes; A53/A54 offsets are relative, A55 pin properties are indexed.</summary>
internal static class DeviceSymbolPlan
{
    internal static WritePlan Create(DevicePlaceArgs args, DevicePlaceRequest request, ISymbolReader? reader)
    {
        if (reader is null)
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Symbol discovery is unavailable; use a Bridge with symbol search.", metadata: new Dictionary<string, string> { ["missing"] = "symbol.search" }); }
        bool box = args.LocationBox is not null;
        bool terminal = args.TerminalStrip is not null;
        string? library = box ? "SPECIAL" : terminal ? "IEC_symbol" : args.Library, name = box ? "SC" : terminal ? "X" : args.Symbol;
        int? variant = box ? 0 : terminal ? args.SymbolVariant ?? 0 : args.SymbolVariant;
        if (!box && !terminal && (!string.IsNullOrWhiteSpace(args.Part) || args.Variant is not null || string.IsNullOrWhiteSpace(args.Library) || string.IsNullOrWhiteSpace(args.Symbol)
            || args.Library!.Any(char.IsControl) || args.Symbol!.Any(char.IsControl) || args.SymbolVariant is null or < 0))
        { throw WriteArguments.Invalid(["/library: specify an exclusive library, symbol and nonnegative symbol_variant source."]); }
        if (args.DisplayProperties is { } displays && (displays.Count > 8 || displays.Any(p => p is null
            || p.PropertyId is not (20214 or 20213 or 20211 or 20215 or 20058 or 20011 or 20027 or 20400 or 20403 or 20301)
            || !Finite(p.OffsetXmm) || !Finite(p.OffsetYmm) || !Finite(args.X + p.OffsetXmm) || !Finite(args.Y + p.OffsetYmm))))
        { throw WriteArguments.Invalid(["/display_properties: require at most 8 allowed property IDs and finite relative offsets."]); }
        if (args.ConnectionDesignations?.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 64 || p.Any(char.IsControl)) == true)
        { throw WriteArguments.Invalid(["/connection_designations: require nonempty text of at most 64 characters without controls."]); }
        List<SymbolSearchItem> found = [.. reader.Read(request.Project.Path, new() { Library = library, NameContains = name })
            .Where(s => string.Equals(s.Library, library, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.Variant == variant)];
        List<SymbolSearchItem> exact = [.. found.Where(s => s.Library == library && s.Name == name)];
        if (exact.Count > 0)
        { found = exact; }
        if (found.Count == 0)
        { throw new BridgeErrorException(ErrorReason.SymbolNotFound, "Select an exact library, symbol and variant from eplan_symbol_search."); }
        if (found.Count > 1)
        { throw ProjectResolver.Ambiguous("symbol", name!, found.Select(s => s.Library + "/" + s.Name), "Select the exact library and symbol spelling from eplan_symbol_search."); }
        SymbolSearchItem symbol = found[0];
        if (args.PlcConnectionPoint is { } point)
        {
            if (symbol.PlcInputDefinition is null || symbol.FunctionCategory != "PLCTerminal")
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Select a PLC graphical variant with a discovered digital-input definition."); }
            args.ConnectionDesignations ??= [point];
            if (!args.ConnectionDesignations.SequenceEqual(new[] { point }, StringComparer.Ordinal))
            { throw WriteArguments.Invalid(["/connection_designations: a digital input requires its single plc_connection_point designation (at most 64 characters)."]); }
        }
        else if (symbol.FunctionCategory == "PLCTerminal")
        { throw WriteArguments.Invalid(["/plc_connection_point: PLC points require an existing card and explicit address, symbolic address and text."]); }
        if (symbol.ConnectionPoints.Any(p => p.Location.Length != 2 || p.Location.Any(v => double.IsNaN(v) || double.IsInfinity(v))))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The symbol has invalid pin geometry; select another symbol with eplan_symbol_search."); }
        if (box ? symbol.Type != "LocationBox" : terminal ? symbol.Kind != "terminal" || symbol.Type != "Function" : symbol.Kind == "plc_box" ? symbol.Library != "SPECIAL" || symbol.Name != "PLCC" : symbol.Kind != "function" || symbol.Type != "Function")
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Select an ordinary function, verified SPECIAL/SC location box or IEC_symbol/X terminal.", metadata: new Dictionary<string, string> { ["missing"] = box ? "device.place.location_box" : terminal ? "device.place.terminal" : "device.place.symbol.function" }); }
        if (args.ConnectionDesignations is { } pins && pins.Length != symbol.ConnectionPoints.Count)
        { throw WriteArguments.Invalid(["/connection_designations: supply exactly one designation per symbol connection point."]); }
        var changes = new List<PlannedChange>
        {
            new() { Action = "create", Kind = box ? "location_box" : "function", Name = request.Page.Name, NewValue = box
                ? FormattableString.Invariant($"{request.Tag}, corners ({args.X}, {args.Y}) to ({args.CornerX}, {args.CornerY}); dashed; affected devices: none; devices keep their own structure")
                : FormattableString.Invariant($"{symbol.Library}/{symbol.Name}, variant {symbol.Variant} as {request.Tag} at ({args.X}, {args.Y}); no part") },
        };
        foreach (DeviceDisplayProperty p in args.DisplayProperties ?? [])
        {
            changes.Add(new()
            {
                Action = "create",
                Kind = "property_placement",
                Name = request.Tag,
                NewValue = FormattableString.Invariant($"{p.PropertyId} offset ({p.OffsetXmm}, {p.OffsetYmm}), page ({args.X + p.OffsetXmm}, {args.Y + p.OffsetYmm})")
            });
        }
        for (int i = 0; i < (args.ConnectionDesignations?.Length ?? 0); i++)
        { changes.Add(new() { Action = "update", Kind = "pin", Name = request.Tag, Property = "20022[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "]", NewValue = args.ConnectionDesignations![i] }); }
        TerminalPlacementData? terminalData = terminal ? DeviceTerminalPlan.Describe(args, request, changes) : null;
        DevicePlcPlan.Describe(args, request, changes);
        string resolved = BridgeJson.FromObject(symbol).ToString(Formatting.None);
        var details = new Dictionary<string, string> { ["symbol"] = resolved, ["tag"] = request.Tag, ["pageSizeKnown"] = request.Page.Width is null ? "false" : "true" };
        if (box)
        { details["locationBox"] = BridgeJson.FromObject(new LocationBoxData { Structure = request.Tag, Bounds = [args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value] }).ToString(Formatting.None); }
        if (terminalData is not null)
        { details["terminal"] = BridgeJson.FromObject(terminalData).ToString(Formatting.None); }
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = changes,
            Fingerprint = Fingerprint.Of([request.StateFingerprint(), resolved, BridgeJson.FromObject(changes).ToString(Formatting.None)]),
            Details = details,
        };
    }

    internal static void CheckBox(DevicePlaceArgs args)
    {
        if (args.LocationBox is not { } structure)
        {
            if (args.CornerX is not null || args.CornerY is not null)
            { throw WriteArguments.Invalid(["/location_box: corner_x and corner_y require a location_box source."]); }
            return;
        }
        if (structure.Length is < 2 or > 65 || structure[0] != '+' || structure.Skip(1).Any(c => !char.IsLetterOrDigit(c) && c is not ('_' or '.'))
            || args.Part is not null || args.Variant is not null || args.Library is not null || args.Symbol is not null || args.SymbolVariant is not null
            || args.Tag is not null || args.DisplayProperties is not null || args.ConnectionDesignations is not null)
        { throw WriteArguments.Invalid(["/location_box: use an exclusive +location identifier (1-64 letters, digits, '_' or '.'); no tag or symbol options."]); }
        if (args.CornerX is not { } x || args.CornerY is not { } y || !Finite(x) || !Finite(y) || x <= args.X || y <= args.Y)
        { throw WriteArguments.Invalid(["/corner_x: corner_x and corner_y must be finite, above and right of x and y."]); }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
