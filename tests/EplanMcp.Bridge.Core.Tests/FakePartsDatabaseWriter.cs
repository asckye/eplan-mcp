using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>The configured parts database as part numbers with their variant counts.</summary>
internal sealed class FakePartsDatabaseWriter : IPartsDatabaseWriter
{
    public ConfiguredDatabase Database { get; set; } = new() { Database = @"$(MD_PARTS)\EPLAN_parts.alk", Path = @"C:\Parts\EPLAN_parts.alk" };

    public Dictionary<string, int> Parts { get; } = new(StringComparer.Ordinal) { ["SIE.3RH2122-1HB40"] = 1, ["PXC.3209510"] = 2 };

    public int Reads { get; private set; }

    public ConfiguredDatabase Configured() => Database;

    public PartsDatabaseState Read()
    {
        Reads++;
        return new PartsDatabaseState { PartCount = Parts.Values.Sum(), PartNumbers = new HashSet<string>(Parts.Keys, StringComparer.Ordinal) };
    }
}
