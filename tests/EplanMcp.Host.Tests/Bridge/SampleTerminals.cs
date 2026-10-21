using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN terminal adapter shaped like spike S21's sample project (api-notes A24): strips in EPLAN's unsorted
/// order, +A2-XD2 with 5 terminals and 3 bridges, +A1-XD4 with 15, and the short name -XD2 in two locations. The
/// terminals of each strip carry ends as EPLAN gives them, an unnamed one included; the first three are bridged in a ring.
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

    public TerminalStripRecord? ReadStrip(string projectPath, string name) =>
        Strips.FirstOrDefault(s => s.Name == name) is not { } strip
            ? null
            : new TerminalStripRecord
            {
                Strip = strip,
                Terminals =
                [
                    .. Enumerable.Range(1, strip.Terminals).Select(i => new TerminalItem
                    {
                        Name = $"{strip.Name}:{i}",
                        Main = i % 2 == 1,
                        Page = "=EA+A2&EFS1/1",
                        Inside = [new ConnectionEnd { Device = "=EA+A2-QA1", Pin = $"{i}" }, new ConnectionEnd { Device = string.Empty, Pin = string.Empty }],
                        Outside = i == 1 ? [new ConnectionEnd { Device = "+B1-WG2", Pin = "1" }] : [],
                        BridgedWith = i <= 3 ? [$"{strip.Name}:{(i % 3) + 1}"] : [],
                    }),
                ],
            };
}
