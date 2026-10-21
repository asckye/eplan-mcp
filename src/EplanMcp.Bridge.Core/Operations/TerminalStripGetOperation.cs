using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TerminalStripGet"/> (docs/tools/eplan_terminal_strip_get.md, section 5). The full name is
/// tried first; then the name without structure (from its first '-', as <c>-XD4</c> in <c>+A1-XD4</c>), ignoring case
/// like a shown device tag, which can match strips in several locations.
/// </summary>
public sealed class TerminalStripGetOperation(IProjectCatalog catalog, ITerminalReader terminals)
    : Operation<TerminalStripGetArgs, TerminalStripGetData>
{
    /// <summary>The most connection ends per side of a terminal (tool spec 3), and the most bridged terminals, returned.</summary>
    public const int MaxEnds = 10;

    public override string Name => OperationNames.TerminalStripGet;

    protected override TerminalStripGetData Execute(TerminalStripGetArgs args)
    {
        string name = Given(args.Strip) ?? throw InvalidArgument("'strip' is required: a terminal strip name such as +A1-XD4 or -XD4.");
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        TerminalStripRecord record = terminals.ReadStrip(project.Path, name)
            ?? (FindByShortName(terminals.ReadStrips(project.Path), name) is { } fullName ? terminals.ReadStrip(project.Path, fullName) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                "The project has no terminal strip with this name. Call eplan_terminal_strip_list with 'name_contains' to find its name.",
                retryable: false,
                new Dictionary<string, string> { ["strip"] = name });

        return new TerminalStripGetData
        {
            Project = project.Name,
            Strip = record.Strip,
            Terminals = [.. Paging.Slice(record.Terminals, args).Select(Shape)],
            Total = record.Terminals.Count,
        };
    }

    /// <summary>A name from its first '-', or the whole name without one: what is left without the structure.</summary>
    internal static string ShortName(string name) => name.IndexOf('-') is var dash and >= 0 ? name.Substring(dash) : name;

    private static string? FindByShortName(IEnumerable<TerminalStripListItem> strips, string name)
    {
        string[] matches = [.. strips.Select(s => s.Name).Where(s => string.Equals(ShortName(s), name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)];
        return matches.Length <= 1
            ? matches.FirstOrDefault()
            : throw ProjectResolver.Ambiguous(
                "terminal strip",
                name,
                matches,
                "Terminal strips in several locations have this name; pass the full name of one of the candidates as 'strip'.");
    }

    private static TerminalItem Shape(TerminalItem terminal)
    {
        int colon = terminal.Name.IndexOf(':');
        terminal.Designation = colon >= 0 && colon < terminal.Name.Length - 1 ? terminal.Name.Substring(colon + 1) : null;
        terminal.Inside = Ends(terminal.Inside);
        terminal.Outside = Ends(terminal.Outside);
        // The adapter leaves the terminal itself out by identity: a level of a multi-level terminal can share its name.
        terminal.BridgedWith = [.. terminal.BridgedWith.Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxEnds)];
        return terminal;
    }

    private static List<ConnectionEnd> Ends(List<ConnectionEnd> ends) =>
        [.. ends.Select(e => ConnectionListOperation.End(e.Device, e.Pin)).OfType<ConnectionEnd>().Take(MaxEnds)];
}
