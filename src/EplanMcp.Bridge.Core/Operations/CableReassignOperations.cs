using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Lets EPLAN assign a cable's connections to its core templates, implemented by the EPLAN adapter layer. Only inside the write's transaction.</summary>
public interface ICableReassigner
{
    /// <summary>Assigns the connections of the cables whose full tag is listed (null: every cable) to matching core templates.</summary>
    public void Reassign(string projectPath, IReadOnlyList<string>? cables, bool overwrite);
}

/// <summary>The checked arguments of eplan_cable_reassign and the connections of the cables in scope.</summary>
internal sealed class CableReassignRequest
{
    public const int MaxCables = 200;
    public const int MaxChanged = 200;

    private CableReassignRequest(OpenProject project, List<string> tags, List<CableConnectionState> state)
    {
        Project = project;
        Tags = tags;
        State = state;
    }

    public OpenProject Project { get; }

    public List<string> Tags { get; }

    public List<CableConnectionState> State { get; }

    public static CableReassignRequest Check(CableReassignArgs args, IProjectCatalog catalog, ICableRenumberer cables, ICableReader reader)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (args.Cables is { Count: < 1 or > MaxCables })
        { violations.Add(FormattableString.Invariant($"'cables' must hold 1 to {MaxCables} full tags; omit it for every cable of the project.")); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        List<string> known = [.. cables.ReadCables(project.Path).Where(c => c.IsMain).Select(c => c.Name).Distinct(StringComparer.Ordinal)];
        foreach (string tag in (args.Cables ?? []).Where(t => !known.Contains(t, StringComparer.Ordinal)))
        { violations.Add("'" + tag + "' is not a cable of the project; eplan_device_list with category Cable lists them."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        List<string> tags = args.Cables is null ? known : [.. args.Cables.Distinct(StringComparer.Ordinal)];
        return new CableReassignRequest(project, tags, Read(project.Path, tags, reader));
    }

    public static List<CableConnectionState> Read(string path, IEnumerable<string> tags, ICableReader reader) =>
        [.. tags.SelectMany(tag => (reader.ReadCable(path, tag, null)?.Cores ?? []).Select(core => new CableConnectionState(
            tag,
            core.StartDevice + ":" + core.StartPin + " -> " + core.EndDevice + ":" + core.EndPin,
            string.Join(" ", new[] { core.Designation, core.Colour, core.CrossSection }.Where(v => !string.IsNullOrEmpty(v))))))];

    public string StateFingerprint(bool overwrite) => Fingerprint.Of(new[] { Project.Path, overwrite ? "overwrite" : "keep" }.Concat(State.Select(s => s.Cable + "|" + s.Connection + "|" + s.Core)));

    public string Scope() => FormattableString.Invariant($"{State.Count} connections of {Tags.Count} cables; only EPLAN knows which core each gets");
}

/// <summary>One connection of a cable as read: where it goes and the core data it has.</summary>
internal sealed class CableConnectionState(string cable, string connection, string core)
{
    public string Cable { get; } = cable;
    public string Connection { get; } = connection;
    public string Core { get; } = core;
}

/// <summary><see cref="OperationNames.CableReassignPlan"/>: the scope, since only EPLAN can tell which core each connection gets. Reads only.</summary>
public sealed class CableReassignPlanOperation(IProjectCatalog catalog, ICableRenumberer cables, ICableReader reader) : Operation<CableReassignArgs, WritePlan>
{
    public override string Name => OperationNames.CableReassignPlan;

    protected override WritePlan Execute(CableReassignArgs args)
    {
        CableReassignRequest request = CableReassignRequest.Check(args, catalog, cables, reader);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = request.Tags.Count == 0 ? [] : [new PlannedChange { Action = "update", Kind = "cables", Name = request.Project.Name, Property = "cores", OldValue = request.Scope(), NewValue = "assigned by EPLAN" }],
            Fingerprint = request.StateFingerprint(args.Overwrite),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.CableReassignExecute"/>: checks the plan's fingerprint again, backs the project up
/// (<see cref="ProjectBackups"/>), lets EPLAN assign the cores in one transaction and reads back what changed.
/// EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class CableReassignExecuteOperation(IProjectCatalog catalog, ICableRenumberer cables, ICableReader reader, ICableReassigner reassigner, IPageReader pages, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<CableReassignArgs, CableReassignData>, IWriteOperation
{
    public const string Tool = "eplan_cable_reassign";

    public override string Name => OperationNames.CableReassignExecute;

    protected override CableReassignData Execute(CableReassignArgs args)
    {
        CableReassignRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one cable.reassign.plan returned."); }
            request = CableReassignRequest.Check(args, catalog, cables, reader);
            if (!string.Equals(request.StateFingerprint(args.Overwrite), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The cables' connections changed after the plan was made, so nothing was assigned. Call eplan_cable_reassign again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new CableReassignData { Project = request.Project.Name };
        if (request.Tags.Count == 0)
        { return data; }
        string path = request.Project.Path;
        data.Backup = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<List<CableCoreChange>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            reassigner.Reassign(path, args.Cables, args.Overwrite);
            ILookup<string, string> before = request.State.ToLookup(s => s.Cable + "|" + s.Connection, s => s.Core, StringComparer.Ordinal);
            return CableReassignRequest.Read(path, request.Tags, reader)
                .Where(s => !before[s.Cable + "|" + s.Connection].Contains(s.Core, StringComparer.Ordinal))
                .Select(s => new CableCoreChange { Cable = s.Cable, Connection = s.Connection, Previous = before[s.Cable + "|" + s.Connection].FirstOrDefault() ?? string.Empty, Core = s.Core })
                .ToList();
        });
        data.ChangedCount = committed.Result.Count;
        data.Changed = [.. committed.Result.Take(CableReassignRequest.MaxChanged)];
        data.Warning = committed.Warning;
        return data;
    }
}
