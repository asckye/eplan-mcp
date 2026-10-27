namespace EplanMcp.Spikes.S43;

internal static class Steps
{
    internal static readonly string[] Groups = ["interruption", "locationbox", "terminals", "cable", "plc", "contacts", "cable_wires"];
    internal static bool Placement(string step) => step == "cable_line" || Groups.Contains(step, StringComparer.Ordinal);
    internal static void Validate(string step, string group, string fail)
    {
        if ((!Placement(step) && step is not ("discover" or "sentinel" or "generate" or "bind" or "cores" or "readback")) ||
            fail is not ("true" or "false") || (fail == "true" && step is "discover" or "readback") ||
            (step == "generate" && !Groups.Contains(group, StringComparer.Ordinal)) ||
            (step is "bind" or "cores" && group is not ("cable" or "cable_wires")) ||
            (step is not ("generate" or "bind" or "cores") && group.Length > 0))
        { throw new ArgumentException("Use discover|sentinel|interruption|locationbox|terminals|cable|cable_wires|cable_line|plc|contacts|generate|bind|cores|readback; --group is required only for generate/bind/cores (bind/cores requires cable or cable_wires); --fail true only for writes."); }
    }
}
