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
        if (!string.IsNullOrWhiteSpace(args.Part) || args.Variant is not null || string.IsNullOrWhiteSpace(args.Library) || string.IsNullOrWhiteSpace(args.Symbol) || args.SymbolVariant is null or < 0)
        { throw WriteArguments.Invalid(["/library: specify an exclusive library, symbol and nonnegative symbol_variant source."]); }
        List<SymbolSearchItem> found = [.. reader.Read(request.Project.Path, new() { Library = args.Library, NameContains = args.Symbol })
            .Where(s => string.Equals(s.Library, args.Library, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Name, args.Symbol, StringComparison.OrdinalIgnoreCase) && s.Variant == args.SymbolVariant)];
        List<SymbolSearchItem> exact = [.. found.Where(s => s.Library == args.Library && s.Name == args.Symbol)];
        if (exact.Count > 0)
        { found = exact; }
        if (found.Count == 0)
        { throw new BridgeErrorException(ErrorReason.SymbolNotFound, "Select an exact library, symbol and variant from eplan_symbol_search."); }
        if (found.Count > 1)
        { throw ProjectResolver.Ambiguous("symbol", args.Symbol!, found.Select(s => s.Library + "/" + s.Name), "Select the exact library and symbol spelling from eplan_symbol_search."); }
        SymbolSearchItem symbol = found[0];
        if (symbol.ConnectionPoints.Any(p => p.Location.Length != 2 || p.Location.Any(v => double.IsNaN(v) || double.IsInfinity(v))))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The symbol has invalid pin geometry; select another symbol with eplan_symbol_search."); }
        if (symbol.Kind != "function" || symbol.Type != "Function")
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Only ordinary function symbols can be planned here; select one from eplan_symbol_search.", metadata: new Dictionary<string, string> { ["missing"] = "device.place.symbol.function" }); }
        if (args.ConnectionDesignations is { } pins && pins.Length != symbol.ConnectionPoints.Count)
        { throw WriteArguments.Invalid(["/connection_designations: supply exactly one designation per symbol connection point."]); }
        var changes = new List<PlannedChange>
        {
            new() { Action = "create", Kind = "function", Name = request.Page.Name, NewValue = FormattableString.Invariant($"{symbol.Library}/{symbol.Name}, variant {symbol.Variant} as {request.Tag} at ({args.X}, {args.Y}); no part") },
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
        string resolved = BridgeJson.FromObject(symbol).ToString(Formatting.None);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = changes,
            Fingerprint = Fingerprint.Of([request.StateFingerprint(), resolved, BridgeJson.FromObject(changes).ToString(Formatting.None)]),
            Details = new() { ["symbol"] = resolved, ["tag"] = request.Tag, ["pageSizeKnown"] = request.Page.Width is null ? "false" : "true" },
        };
    }
}
