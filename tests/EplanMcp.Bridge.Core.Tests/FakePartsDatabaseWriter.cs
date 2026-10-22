using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// The configured parts database as part numbers with their variant counts. <see cref="OnImport"/> stands for what EPLAN
/// does to it; by default nothing. <see cref="ImportFailure"/> and <see cref="ReadFailure"/> make those calls fail.
/// </summary>
internal sealed class FakePartsDatabaseWriter : IPartsDatabaseWriter
{
    public ConfiguredDatabase Database { get; set; } = new() { Database = @"$(MD_PARTS)\EPLAN_parts.alk", Path = @"C:\Parts\EPLAN_parts.alk" };

    public Dictionary<string, int> Parts { get; } = new(StringComparer.Ordinal) { ["SIE.3RH2122-1HB40"] = 1, ["PXC.3209510"] = 2 };

    public List<(string File, string Mode, string MatchBy)> Imports { get; } = [];

    public Action<Dictionary<string, int>>? OnImport { get; set; }

    public Exception? ImportFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public int Reads { get; private set; }

    public ConfiguredDatabase Configured() => Database;

    public PartsDatabaseState Read()
    {
        Reads++;
        if (ReadFailure is not null && Imports.Count > 0)
        {
            throw ReadFailure;
        }

        return new PartsDatabaseState { PartCount = Parts.Values.Sum(), PartNumbers = new HashSet<string>(Parts.Keys, StringComparer.Ordinal) };
    }

    public void Import(string file, string mode, string matchBy)
    {
        Imports.Add((file, mode, matchBy));
        if (ImportFailure is not null)
        {
            throw ImportFailure;
        }

        OnImport?.Invoke(Parts);
    }
}
