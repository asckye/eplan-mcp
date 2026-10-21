using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of terminal.renumber.plan and terminal.renumber.execute and what they read; both build them the
/// same way, so they agree on the scheme, the scope and the fingerprint (docs/tools/eplan_terminal_renumber.md, section 5).
/// </summary>
internal sealed class TerminalRenumberRequest
{
    public const int MaxStrips = 50;
    public const int MaxChanged = 200;
    public const int MaxListed = 50;

    private TerminalRenumberRequest(OpenProject project, TerminalRenumberArgs args, string scheme)
    {
        Project = project;
        Strips = args.Strips;
        Scheme = scheme;
        OnlyUnnumbered = args.OnlyUnnumbered;
        Start = args.Start;
        Step = args.Step;
    }

    public OpenProject Project { get; }

    /// <summary>The strips in scope; null for the whole project.</summary>
    public IReadOnlyList<string>? Strips { get; }

    public string Scheme { get; }

    public bool OnlyUnnumbered { get; }

    public int Start { get; }

    public int Step { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project, then the scheme.</summary>
    public static TerminalRenumberRequest Check(TerminalRenumberArgs args, IProjectCatalog catalog, ITerminalRenumberer renumberer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Strips is { } strips)
        {
            if (strips.Count is < 1 or > MaxStrips)
            {
                violations.Add($"'strips' must hold 1 to {Number(MaxStrips)} terminal strip names; omit it for the whole project.");
            }

            for (int i = 0; i < strips.Count && i < MaxStrips; i++)
            {
                int same = strips.FindIndex(0, i, s => string.Equals(s, strips[i], StringComparison.Ordinal));
                AddIf(violations, string.IsNullOrWhiteSpace(strips[i]), $"strips[{Number(i)}] is empty; give a strip name such as +A1-XD1 (eplan_terminal_strip_list).");
                AddIf(violations, same >= 0 && !string.IsNullOrWhiteSpace(strips[i]), $"strips[{Number(i)}] repeats strips[{Number(same)}].");
            }
        }

        AddIf(violations, string.IsNullOrWhiteSpace(args.Scheme), "'scheme' is required: a terminal numbering scheme such as Numerisch.");
        AddIf(violations, args.Start < 1, "'start' must be at least 1.");
        AddIf(violations, args.Step < 1, "'step' must be at least 1.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new TerminalRenumberRequest(project, args, SchemeChoice.Require(args.Scheme!.Trim(), renumberer.Schemes(), "terminal"));
    }

    /// <summary>The terminals in scope, the strips by their exact names; DEVICE_NOT_FOUND names every strip that is missing.</summary>
    public List<PlacedFunction> Scope(ITerminalRenumberer renumberer)
    {
        if (Strips is null)
        {
            return [.. renumberer.ReadTerminals(Project.Path, null) ?? []];
        }

        var terminals = new List<PlacedFunction>();
        var missing = new List<string>();
        foreach (string strip in Strips)
        {
            if (renumberer.ReadTerminals(Project.Path, strip) is { } onStrip)
            {
                terminals.AddRange(onStrip);
            }
            else
            {
                missing.Add(strip);
            }
        }

        return missing.Count == 0
            ? terminals
            : throw new BridgeErrorException(
                ErrorReason.DeviceNotFound,
                $"Project '{Project.Name}' has no terminal strip with exactly these names (letter case counts): {string.Join(", ", missing)}. Nothing was numbered. Use eplan_terminal_strip_list to find the names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["missing"] = string.Join("\n", missing) });
    }

    /// <summary>How many terminals EPLAN is asked to number: every one in scope, or with <see cref="OnlyUnnumbered"/> those with '?'.</summary>
    public int ToNumber(IEnumerable<PlacedFunction> scope) => scope.Count(IsToNumber);

    /// <summary>Whether EPLAN is asked to number <paramref name="terminal"/>, one of the scope; no other terminal may change.</summary>
    public bool IsToNumber(PlacedFunction terminal) => !OnlyUnnumbered || Unnumbered(terminal.Name);

    /// <summary>The change list's text, e.g. <c>all terminal designations on 1 strip (12 terminals), scheme: Numerisch, start 1, step 1</c>.</summary>
    public string Describe(int terminals) =>
        (OnlyUnnumbered ? "terminal designations containing '?'" : "all terminal designations")
        + (Strips is null ? " in the project" : " on " + Number(Strips.Count) + (Strips.Count == 1 ? " strip" : " strips"))
        + $" ({Number(terminals)} {(terminals == 1 ? "terminal" : "terminals")}), scheme: {Scheme}, start {Number(Start)}, step {Number(Step)}";

    /// <summary>Whether the designation, the part of the name after the strip's <c>:</c>, contains '?' (tool spec 5).</summary>
    public static bool Unnumbered(string name)
    {
        int colon = name.IndexOf(':');
        return colon >= 0 && name.IndexOf('?', colon + 1) >= 0;
    }

    /// <summary>The plan's state: every terminal in scope by its session identifier and full name.</summary>
    public static string FingerprintOf(IEnumerable<PlacedFunction> scope) => Fingerprint.Of(scope.Select(t => t.Id + "\t" + t.Name));

    /// <summary>
    /// The terminals whose name changed, matched by session identifier, each pair once (the levels of a terminal can share
    /// a name); and how many terminals of <paramref name="before"/> had no identifier left afterwards.
    /// </summary>
    public static (List<RenamedObject> Changed, int Unmatched) Changes(IEnumerable<PlacedFunction> before, IEnumerable<PlacedFunction> after)
    {
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PlacedFunction terminal in after)
        {
            now[terminal.Id] = terminal.Name;
        }

        var changed = new List<RenamedObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int unmatched = 0;
        foreach (PlacedFunction terminal in before)
        {
            if (!now.TryGetValue(terminal.Id, out string? name))
            {
                unmatched++;
            }
            else if (!string.Equals(terminal.Name, name, StringComparison.Ordinal) && seen.Add(terminal.Name + "\n" + name))
            {
                changed.Add(new RenamedObject { From = terminal.Name, To = name });
            }
        }

        return (changed, unmatched);
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
