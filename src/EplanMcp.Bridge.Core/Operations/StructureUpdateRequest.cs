using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_structure_update and what they would change.</summary>
internal sealed class StructureUpdateRequest
{
    public const int MaxIdentifiers = 200;
    private const int MaxLength = 500;

    private StructureUpdateRequest(OpenProject project, List<Change> changes, List<OrderChange> sorts)
    {
        Project = project;
        Changes = changes;
        Sorts = sorts;
    }

    public OpenProject Project { get; }

    /// <summary>The identifiers to create or describe, in the call's order.</summary>
    public List<Change> Changes { get; }

    /// <summary>The blocks to sort, each with the order that is new.</summary>
    public List<OrderChange> Sorts { get; }

    public static StructureUpdateRequest Check(StructureUpdateArgs args, IProjectCatalog catalog, IStructureIdentifiers identifiers, ITextTranslator translator)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<StructureIdentifierValue> given = args.Identifiers ?? [];
        List<string> order = args.Order ?? [];
        if ((given.Count < 1 && order.Count < 1) || given.Count > MaxIdentifiers || order.Count > MaxIdentifiers)
        { violations.Add(FormattableString.Invariant($"Give 'identifiers', 'order' or both, each at most {MaxIdentifiers} entries.")); }
        if (order.Any(n => !StructureNames.IsValid(n)))
        { violations.Add("'order' holds full names with their block's prefix (=, ==, ++, +, &, #), such as +S1."); }
        if (order.Distinct(StringComparer.Ordinal).Count() != order.Count)
        { violations.Add("'order': give each identifier once."); }
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
        IReadOnlyList<StructureIdentifier> read = identifiers.Read(project.Path);
        Dictionary<string, string> stored = read.ToDictionary(i => i.Name, i => i.Description, StringComparer.Ordinal);
        var changes = new List<Change>();
        foreach (StructureIdentifierValue item in given)
        {
            bool exists = stored.TryGetValue(item.Name!, out string? before);
            string? wanted = args.Translate && item.Description is { Length: > 0 } text && !TextLanguages.IsSerialized(text)
                ? translator.Translate(project.Path, text)
                : item.Description;
            if (!exists || (wanted is { } after && !TextLanguages.SameValue(before!, after)))
            { changes.Add(new Change(item.Name!, !exists, before ?? string.Empty, wanted)); }
        }
        var known = new HashSet<string>(stored.Keys.Concat(given.Select(g => g.Name!)), StringComparer.Ordinal);
        foreach (string name in order.Where(n => !known.Contains(n)))
        { violations.Add("'order' names " + name + ", which the project does not have and 'identifiers' does not create."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        var sorts = new List<OrderChange>();
        foreach (IGrouping<string, string> block in order.GroupBy(StructureNames.Prefix))
        {
            List<string> present = [.. read.Select(i => i.Name).Where(n => StructureNames.Prefix(n) == block.Key)];
            present.AddRange(changes.Where(c => c.Create && StructureNames.Prefix(c.Name) == block.Key).Select(c => c.Name));
            if (!present.Take(block.Count()).SequenceEqual(block, StringComparer.Ordinal))
            { sorts.Add(new OrderChange(block.Key, [.. block], string.Join(" ", present))); }
        }
        return new StructureUpdateRequest(project, changes, sorts);
    }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }
        .Concat(Changes.SelectMany(c => new[] { c.Name, c.Before, c.After ?? "\0" }))
        .Concat(Sorts.SelectMany(s => new[] { s.Before, s.After })));

    /// <summary>One block to sort: the names that go first, and the block's whole order now, joined with spaces. EPLAN decides the order of the others (api-notes A107).</summary>
    public sealed class OrderChange(string prefix, List<string> names, string before)
    {
        public string Prefix { get; } = prefix;
        public List<string> Names { get; } = names;
        public string Before { get; } = before;
        public string After => string.Join(" ", Names);
    }

    /// <summary>One identifier to change; a null <see cref="After"/> keeps the description.</summary>
    public sealed class Change(string name, bool create, string before, string? after)
    {
        public string Name { get; } = name;
        public bool Create { get; } = create;
        public string Before { get; } = before;
        public string? After { get; } = after;
    }
}
