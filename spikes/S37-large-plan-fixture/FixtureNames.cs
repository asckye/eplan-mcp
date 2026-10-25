namespace EplanMcp.Spikes.S37;

internal static class FixtureNames
{
    // Both the higher-level function (=) and the location (+) get the copy group, so functions that inherit either one
    // from the page receive new full names instead of merging with the source page's devices.
    internal static string Target(string name, string plant, string location, string group) =>
        WithLocation(WithPlant(name, plant, group), location, group);

    private static string WithPlant(string name, string plant, string group)
    {
        if (plant.Length == 0)
        {
            int at = name.IndexOfAny(['+', '&', '/']);
            if (at < 0) { throw new ArgumentException("Unsupported source page name."); }
            return name.Insert(at, "=" + group);
        }

        string token = "=" + plant;
        int index = name.IndexOf(token, StringComparison.Ordinal);
        while (index > 0 && name[index - 1] == '=')
        {
            index = name.IndexOf(token, index + 1, StringComparison.Ordinal);
        }
        if (index < 0)
        { throw new ArgumentException("Cannot locate the source higher-level function in its full page name."); }
        return name.Insert(index + 1, group + ".");
    }

    private static string WithLocation(string name, string location, string group)
    {
        if (location.Length == 0)
        {
            int at = name.IndexOfAny(['&', '/']);
            if (at < 0) { throw new ArgumentException("Unsupported source page name."); }
            return name.Insert(at, "+" + group);
        }

        int index = name.IndexOf("+" + location, StringComparison.Ordinal);
        if (index < 0)
        { throw new ArgumentException("Cannot locate the source location in its full page name."); }
        return name.Insert(index + 1, group + ".");
    }
}
