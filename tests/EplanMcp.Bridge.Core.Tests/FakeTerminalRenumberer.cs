using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Terminal strips and their terminals, numbered the way S21 found <c>Renumber.Terminals</c> numbers them (api-notes A24):
/// the schemes are S21's and the names in <see cref="Numbers"/> change in the strips given (all strips without any).
/// <see cref="Refuse"/> stands for EPLAN's InvalidScheme with S12's message, <see cref="NewIds"/> for EPLAN giving the
/// terminals new session identifiers. Records every numbering.
/// </summary>
internal sealed class FakeTerminalRenumberer : ITerminalRenumberer
{
    public List<string> SchemeNames { get; } = ["Gerätedefinition", "Initiatoren", "Numerisch", SchemeChoice.Internal];

    /// <summary>Strip name → its terminals in strip order.</summary>
    public Dictionary<string, List<PlacedFunction>> Strips { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Numbers { get; } = new(StringComparer.Ordinal);

    public bool NewIds { get; set; }

    /// <summary>When true, EPLAN refuses the scheme while numbering, as InvalidScheme does.</summary>
    public bool Refuse { get; set; }

    public List<(IReadOnlyList<string>? Strips, bool OnlyUnnumbered, string Scheme, int Start, int Step)> Renumbers { get; } = [];

    public IReadOnlyList<string> Schemes() => SchemeNames;

    public IReadOnlyList<PlacedFunction>? ReadTerminals(string projectPath, string? strip) =>
        strip is null ? [.. Strips.Values.SelectMany(t => t).Select(Copy)]
        : Strips.TryGetValue(strip, out List<PlacedFunction>? terminals) ? [.. terminals.Select(Copy)]
        : null;

    public void Renumber(string projectPath, IReadOnlyList<string>? strips, bool onlyUnnumbered, string scheme, int start, int increment)
    {
        Renumbers.Add((strips, onlyUnnumbered, scheme, start, increment));
        if (Refuse)
        {
            throw SchemeChoice.Refused(scheme, FakeDeviceRenumberer.InvalidSchemeMessage, "terminal", new InvalidOperationException(FakeDeviceRenumberer.InvalidSchemeMessage));
        }

        foreach (PlacedFunction terminal in Strips.Where(s => strips is null || strips.Contains(s.Key)).SelectMany(s => s.Value))
        {
            if ((!onlyUnnumbered || TerminalRenumberRequest.Unnumbered(terminal.Name)) && Numbers.TryGetValue(terminal.Name, out string? name))
            {
                terminal.Name = name;
            }

            if (NewIds)
            {
                terminal.Id += "'";
            }
        }
    }

    private static PlacedFunction Copy(PlacedFunction t) => new() { Name = t.Name, IsMain = t.IsMain, Page = t.Page, Id = t.Id };
}
