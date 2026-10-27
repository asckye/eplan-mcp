namespace EplanMcp.Spikes.S36.Part2;

internal static class Steps
{
    internal static void Validate(string step, string fail)
    {
        if (step is not ("sentinel" or "insert" or "closure" or "mutate" or "writeback" or "readback") ||
            fail is not ("true" or "false") || (fail == "true" && step is not ("writeback" or "mutate")))
        { throw new ArgumentException("Use sentinel|insert|closure|mutate|writeback|readback; --fail true is allowed only for writeback/mutate."); }
    }
}
