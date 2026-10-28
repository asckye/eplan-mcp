using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class DevicePlcPlan
{
    internal static void Check(DevicePlaceArgs args)
    {
        if (args.PlcConnectionPoint is null && args.PlcAddress is null && args.PlcSymbolicAddress is null && args.PlcText is null)
        { return; }
        if (args.Library is null || args.TerminalStrip is not null || args.LocationBox is not null
            || new[] { args.PlcConnectionPoint, args.PlcAddress, args.PlcSymbolicAddress, args.PlcText }.Any(s => string.IsNullOrWhiteSpace(s) || s!.Length > 256 || s.Any(char.IsControl))
            || args.PlcConnectionPoint is { Length: > 64 } || args.PlcConnectionPoint!.StartsWith(":", StringComparison.Ordinal))
        { throw WriteArguments.Invalid(["/plc_connection_point: require a symbol source, existing PLC tag and four nonempty PLC fields without controls (point at most 64 characters, others 256); omit a leading colon."]); }
    }

    internal static void Describe(DevicePlaceArgs args, DevicePlaceRequest request, List<PlannedChange> changes)
    {
        if (args.PlcConnectionPoint is null)
        { return; }
        string parent = request.Tag.Substring(0, request.Tag.Length - args.PlcConnectionPoint.Length - 1);
        if (!request.Existing.Contains(parent, StringComparer.OrdinalIgnoreCase))
        { throw new BridgeErrorException(ErrorReason.DeviceNotFound, "Create the PLC card explicitly first, then use its tag for the connection point."); }
        changes[0].Kind = "plc_connection_point";
        changes[0].NewValue += "; existing card " + parent + "; EPLAN also creates a parentless PLCTerminal (A55)";
        foreach ((string id, string? value) in new[] { ("20400", args.PlcAddress), ("20402", args.PlcSymbolicAddress), ("20011 (??_??)", args.PlcText) })
        { changes.Add(new() { Action = "update", Kind = "plc_property", Name = request.Tag, Property = id, NewValue = value }); }
    }
}
