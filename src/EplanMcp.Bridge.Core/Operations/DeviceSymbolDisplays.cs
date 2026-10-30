using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class DeviceSymbolDisplays
{
    internal static void Verify(DevicePlaceArgs args, string tag, string page, PlcChannel? plc, IReadOnlyList<DeviceSymbolDisplay> actual)
    {
        List<DeviceDisplayProperty> requested = args.DisplayProperties ?? [];
        for (int i = 0; i < requested.Count; i++)
        {
            DeviceDisplayProperty display = requested[i];
            DeviceSymbolDisplay[] matches = [.. actual.Where(p => p.PropertyId == display.PropertyId
                && p.Offset.SequenceEqual(new[] { display.OffsetXmm, display.OffsetYmm }))];
            int count = requested.Count(p => p.PropertyId == display.PropertyId && p.OffsetXmm == display.OffsetXmm && p.OffsetYmm == display.OffsetYmm);
            string? expected = plc is null ? null : display.PropertyId switch
            {
                20400 => plc.Address ?? string.Empty,
                20403 => plc.SymbolicAddress ?? string.Empty,
                20011 => plc.Text ?? string.Empty,
                20301 => plc.CardReference ?? string.Empty,
                _ => null
            };
            if (matches.Length != count || matches.Any(p => !p.OnPlacedFunction || p.Page != page || p.ParentTag is null
                || !DevicePlaceRequest.SameName(tag, p.ParentTag) || !p.FormatMatches
                || !p.Position.SequenceEqual(new[] { args.X + display.OffsetXmm, args.Y + display.OffsetYmm })
                || expected is not null && p.Text != expected
                || display.PropertyId is 20214 or 20213 or 20211 or 20215 && p.Text.IndexOf(tag.Substring(tag.LastIndexOf('-')), StringComparison.Ordinal) < 0))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                    FormattableString.Invariant($"/display_properties/{i}: property {display.PropertyId} is missing from the placed point or its parent, page, position, format or text differs. Nothing was committed; read the page and re-plan."));
            }
        }
    }
}
