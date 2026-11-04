using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class DevicePlcPlan
{
    // A78: the Name suffix is 20022, not 20406. A composite Name changes the pin, not the plug.
    internal static string Suffix(DevicePlaceArgs args) => args.PlcConnectionPoint!;
    internal static string Parent(DevicePlaceArgs args, string tag) => tag.Substring(0, tag.Length - Suffix(args).Length - 1);

    internal static bool ReusesPoint(string tag, IReadOnlyList<string> taken, DeviceNames? device, out string? warning)
    {
        warning = null;
        if (taken.Count == 0 || taken.Any(n => !DevicePlaceRequest.SameName(tag, n, ignoreCase: true)))
        { return false; }
        DeviceNames.NamedFunction[] functions = [.. device?.Functions.Where(f => DevicePlaceRequest.SameName(tag, f.Name, ignoreCase: true)) ?? []];
        if (functions.Any(f => f.Category is not (null or "PLCTerminal" or "BusTerminal")))
        { return false; }
        if (functions.Length == 0 || functions.Any(f => f.Category is null))
        { warning = "Existing PLC point category is unreadable; verify both representations with eplan_page_get after placement."; }
        return true;
    }

    internal static void Check(DevicePlaceArgs args)
    {
        if (args.PlcPlugDesignation is { } plug && (args.PlcConnectionPoint is null || string.IsNullOrWhiteSpace(plug) || plug.Length > 64 || plug.Any(char.IsControl)))
        { throw WriteArguments.Invalid(["/plc_plug_designation: require a complete PLC source and 1..64 nonblank characters without controls."]); }
        if (args.PlcText is { } text && TextLanguages.Stored(text).Count > 0)
        { throw WriteArguments.Invalid(["/plc_text: use plain language-independent text (??_??), without serialized language entries; do not mix it with real languages (A54)."]); }
        if (args.PlcConnectionPoint is null && args.PlcAddress is null && args.PlcSymbolicAddress is null && args.PlcText is null)
        { return; }
        if (args.Library is null || args.TerminalStrip is not null || args.LocationBox is not null
            || string.IsNullOrWhiteSpace(args.PlcConnectionPoint)
                || new[] { args.PlcConnectionPoint, args.PlcAddress, args.PlcSymbolicAddress, args.PlcText }.Any(s => s is not null && (string.IsNullOrWhiteSpace(s) || s.Length > 256 || s.Any(char.IsControl)))
            || args.PlcConnectionPoint is { Length: > 64 } || args.PlcConnectionPoint!.StartsWith(":", StringComparison.Ordinal))
        { throw WriteArguments.Invalid(["/plc_connection_point: require a symbol source, existing PLC tag and a nonempty point and optional address, symbolic address and text without controls (point at most 64 characters, others 256); omit a leading colon."]); }
    }

    internal static void Describe(DevicePlaceArgs args, DevicePlaceRequest request, List<PlannedChange> changes)
    {
        if (args.PlcConnectionPoint is null)
        { return; }
        string parent = Parent(args, request.Tag);
        if (!request.Existing.Contains(parent, StringComparer.OrdinalIgnoreCase))
        { throw new BridgeErrorException(ErrorReason.DeviceNotFound, "Create the PLC card explicitly first, then use its tag for the connection point."); }
        changes[0].Kind = "plc_connection_point";
        changes[0].NewValue += "; existing card " + parent;
        if (args.PlcPlugDesignation is { } plug)
        { changes.Add(new() { Action = "update", Kind = "plc_property", Name = request.Tag, Property = "20406", NewValue = plug }); }
        if (args.DisplayProperties?.Any(p => p.PropertyId is 20300 or 20301) == true)
        { changes.Add(new() { Action = "update", Kind = "plc_property", Name = request.Tag, Property = "20021", NewValue = "2" }); }
        foreach ((string id, string? value) in new[] { ("20400", args.PlcAddress), ("20402", args.PlcSymbolicAddress), ("20011 (??_??)", args.PlcText) })
        { if (value is not null) { changes.Add(new() { Action = "update", Kind = "plc_property", Name = request.Tag, Property = id, NewValue = value }); } }
    }
}
