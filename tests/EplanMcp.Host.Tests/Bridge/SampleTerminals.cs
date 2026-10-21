using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN terminal adapter shaped like spike S21's sample project (api-notes A24): strips in EPLAN's unsorted
/// order, +A2-XD2 with 5 terminals and 3 bridges, +A1-XD4 with 15, and the short name -XD2 in two locations.
/// </summary>
internal sealed class SampleTerminals : ITerminalReader
{
    public List<TerminalStripListItem> Strips { get; } =
    [
        new() { Name = "+A2-XD2", Terminals = 5, Bridges = 3 },
        new() { Name = "+A1-XD4", Terminals = 15 },
        new() { Name = "+B2.X1-XZ1", Terminals = 33 },
        new() { Name = "+A1-XD2", Terminals = 4 },
    ];

    public IReadOnlyList<TerminalStripListItem> ReadStrips(string projectPath) => Strips;
}
