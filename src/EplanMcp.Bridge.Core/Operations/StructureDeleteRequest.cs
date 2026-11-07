using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_structure_delete and the unused identifiers they would remove.</summary>
internal sealed class StructureDeleteRequest
{
    public const int MaxIdentifiers = 200;

    private StructureDeleteRequest(OpenProject project, List<StructureIdentifier> removing)
    {
        Project = project;
        Removing = removing;
    }

    public OpenProject Project { get; }

    public List<StructureIdentifier> Removing { get; }

    public static StructureDeleteRequest Check(StructureDeleteArgs args, IProjectCatalog catalog, IStructureIdentifiers identifiers)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Identifiers is { Count: < 1 or > MaxIdentifiers })
        { violations.Add(FormattableString.Invariant($"'identifiers' must hold 1 to {MaxIdentifiers} entries; omit it for every unused identifier.")); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<StructureIdentifier> stored = identifiers.Read(project.Path);
        if (args.Identifiers is null)
        { return new StructureDeleteRequest(project, [.. stored.Where(i => !i.Used)]); }
        var removing = new List<StructureIdentifier>();
        foreach (string name in args.Identifiers.Distinct(StringComparer.Ordinal))
        {
            StructureIdentifier? found = stored.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.Ordinal));
            if (found is null)
            { violations.Add("'" + name + "' is not a structure identifier of the project; eplan_structure_list lists them."); }
            else if (found.Used)
            { violations.Add("'" + name + "' is in use, so it stays: EPLAN removes only unused identifiers."); }
            else
            { removing.Add(found); }
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        return new StructureDeleteRequest(project, removing);
    }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(Removing.SelectMany(i => new[] { i.Name, i.Description })));
}
