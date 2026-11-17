using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_structure_rename and the renames that change anything.</summary>
internal sealed class StructureRenameRequest
{
    public const int MaxRenames = 50;

    private StructureRenameRequest(OpenProject project, List<(string Name, string NewName, string Description)> renames)
    {
        Project = project;
        Renames = renames;
    }

    public OpenProject Project { get; }

    public List<(string Name, string NewName, string Description)> Renames { get; }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(Renames.SelectMany(r => new[] { r.Name, r.NewName, r.Description })));

    public static StructureRenameRequest Check(StructureRenameArgs args, IProjectCatalog catalog, IStructureIdentifiers identifiers)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<StructureRenameValue> given = args.Renames ?? [];
        if (given.Count < 1 || given.Count > MaxRenames)
        { violations.Add(FormattableString.Invariant($"'renames' must hold 1 to {MaxRenames} entries.")); }
        for (int i = 0; i < given.Count; i++)
        {
            string at = FormattableString.Invariant($"/renames/{i}");
            string? name = given[i].Name, newName = given[i].NewName;
            if (!StructureNames.IsValid(name) || !StructureNames.IsValid(newName))
            { violations.Add(at + ": give full names with their block's prefix (=, ==, ++, +, &, #), such as +S1."); }
            else if (Parent(name!) != Parent(newName!))
            { violations.Add(at + "/new_name: stay in the same block and under the same parent."); }
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        Dictionary<string, string> stored = identifiers.Read(project.Path).ToDictionary(i => i.Name, i => i.Description, StringComparer.Ordinal);
        var renames = new List<(string, string, string)>();
        foreach (StructureRenameValue item in given.Where(g => !string.Equals(g.Name, g.NewName, StringComparison.Ordinal)))
        {
            if (!stored.TryGetValue(item.Name!, out string? description))
            { violations.Add("'" + item.Name + "' is not a structure identifier of the project; eplan_structure_list lists them."); }
            else if (stored.ContainsKey(item.NewName!))
            { violations.Add("'" + item.NewName + "' already exists in the project."); }
            else
            { renames.Add((item.Name!, item.NewName!, description)); }
        }
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new StructureRenameRequest(project, renames);
    }

    private static string Parent(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(0, dot) : StructureNames.Prefix(name);
    }
}

/// <summary><see cref="OperationNames.StructureRenamePlan"/>: one <c>name</c> update per identifier. Reads only.</summary>
public sealed class StructureRenamePlanOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers) : Operation<StructureRenameArgs, WritePlan>
{
    public override string Name => OperationNames.StructureRenamePlan;

    protected override WritePlan Execute(StructureRenameArgs args)
    {
        StructureRenameRequest request = StructureRenameRequest.Check(args, catalog, identifiers);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Renames.Select(r => new PlannedChange { Action = "update", Kind = "structure", Name = r.Name, Property = "name", OldValue = r.Name, NewValue = r.NewName })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.StructureRenameExecute"/>: checks the plan's fingerprint again, backs the project up
/// (<see cref="ProjectBackups"/>), renames in one transaction and reads back; a name that did not change rolls all back.
/// </summary>
public sealed class StructureRenameExecuteOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers, IPageReader pages, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<StructureRenameArgs, StructureRenameData>, IWriteOperation
{
    public const string Tool = "eplan_structure_rename";

    public override string Name => OperationNames.StructureRenameExecute;

    protected override StructureRenameData Execute(StructureRenameArgs args)
    {
        StructureRenameRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one structure.rename.plan returned."); }
            request = StructureRenameRequest.Check(args, catalog, identifiers);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's structure identifiers changed after the plan was made, so nothing was renamed. Call eplan_structure_rename again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new StructureRenameData { Project = request.Project.Name };
        if (request.Renames.Count == 0)
        { return data; }
        string path = request.Project.Path;
        data.Backup = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<List<StructureIdentifier>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            foreach ((string name, string newName, _) in request.Renames)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                identifiers.Rename(path, name, newName);
            }
            Dictionary<string, StructureIdentifier> stored = identifiers.Read(path).ToDictionary(i => i.Name, StringComparer.Ordinal);
            var read = new List<StructureIdentifier>();
            foreach ((string name, string newName, _) in request.Renames)
            {
                if (stored.ContainsKey(name) || !stored.TryGetValue(newName, out StructureIdentifier? renamed))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not rename a structure identifier, so nothing was committed. metadata.identifier names it.",
                        retryable: false, new Dictionary<string, string> { ["identifier"] = name });
                }
                read.Add(renamed);
            }
            return read;
        });
        data.Identifiers = committed.Result;
        data.Warning = committed.Warning;
        return data;
    }
}
