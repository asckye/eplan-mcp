using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_structure_update and what they would change.</summary>
internal sealed class StructureUpdateRequest
{
    public const int MaxIdentifiers = 200;
    private const int MaxLength = 500;

    private StructureUpdateRequest(OpenProject project, List<Change> changes)
    {
        Project = project;
        Changes = changes;
    }

    public OpenProject Project { get; }

    /// <summary>The identifiers to create or describe, in the call's order.</summary>
    public List<Change> Changes { get; }

    public static StructureUpdateRequest Check(StructureUpdateArgs args, IProjectCatalog catalog, IStructureIdentifiers identifiers)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<StructureIdentifierValue> given = args.Identifiers ?? [];
        if (given.Count < 1 || given.Count > MaxIdentifiers)
        { violations.Add(FormattableString.Invariant($"'identifiers' must hold 1 to {MaxIdentifiers} entries.")); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < given.Count; i++)
        {
            string at = FormattableString.Invariant($"/identifiers/{i}");
            if (!StructureNames.IsValid(given[i].Name))
            { violations.Add(at + "/name: give the full name with its block's prefix (=, ==, ++, +, &, #), such as +S1."); }
            else if (!seen.Add(given[i].Name!))
            { violations.Add(at + "/name: give each identifier once."); }
            if (given[i].Description is { } text && (text.Length > MaxLength || text.Any(char.IsControl)))
            { violations.Add(at + "/description: supply text of at most 500 characters without controls; empty clears it."); }
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        Dictionary<string, string> stored = identifiers.Read(project.Path).ToDictionary(i => i.Name, i => i.Description, StringComparer.Ordinal);
        var changes = new List<Change>();
        foreach (StructureIdentifierValue item in given)
        {
            bool exists = stored.TryGetValue(item.Name!, out string? before);
            if (!exists || (item.Description is { } after && !TextLanguages.SameValue(before!, after)))
            { changes.Add(new Change(item.Name!, !exists, before ?? string.Empty, item.Description)); }
        }
        return new StructureUpdateRequest(project, changes);
    }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(Changes.SelectMany(c => new[] { c.Name, c.Before, c.After ?? "\0" })));

    /// <summary>One identifier to change; a null <see cref="After"/> keeps the description.</summary>
    public sealed class Change(string name, bool create, string before, string? after)
    {
        public string Name { get; } = name;
        public bool Create { get; } = create;
        public string Before { get; } = before;
        public string? After { get; } = after;
    }
}
