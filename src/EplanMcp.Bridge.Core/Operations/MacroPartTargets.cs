namespace EplanMcp.Bridge.Core.Operations;

public static class MacroPartTargets
{
    public static string[] Select(IEnumerable<string> tags)
    {
        var devices = tags.Select(name => (Name: name, Tag: name.Split(':')[0])).ToArray();
        return [.. devices.Where(device => !devices.Any(parent =>
            device.Tag.StartsWith(parent.Tag + "-", StringComparison.Ordinal))).Select(device => device.Name)];
    }
}
