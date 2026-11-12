using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Removes placed layout objects, implemented by the EPLAN adapter; main thread, inside the operation's transaction.</summary>
public interface ILayoutRemover
{
    /// <summary>Removes the rails, ducts and device placements with these object IDs, children first.</summary>
    public void Remove(string projectPath, IReadOnlyList<long> ids);
}

/// <summary>The checked arguments of eplan_layout_remove, shared by its plan and its execution.</summary>
internal sealed class LayoutRemoveRequest
{
    public const int MaxItems = 200;

    public OpenProject Project { get; private set; } = new();
    public string Space { get; private set; } = string.Empty;
    public List<LayoutObject> Targets { get; private set; } = [];
    public string State { get; private set; } = string.Empty;

    /// <summary>A rail, a duct, or a device placement (a component named after its schematic device).</summary>
    public static bool Removable(LayoutObject o) => o.Kind is "MountingRail" or "Duct" || (o.Kind == "Component" && o.Tag is not null);

    public static LayoutRemoveRequest Check(LayoutRemoveArgs args, IProjectCatalog catalog, ILayoutReader layout)
    {
        var violations = new List<string>();
        string space = (args.Space ?? string.Empty).Trim().TrimStart('+');
        if (space.Length == 0)
        { violations.Add("'space' is required: an installation space designation such as UC1."); }
        List<long> ids = args.Ids ?? [];
        if (ids.Count is < 1 or > MaxItems)
        { violations.Add($"'ids' must hold 1 to {MaxItems.ToString(CultureInfo.InvariantCulture)} object IDs from eplan_layout_get."); }
        if (ids.Distinct().Count() != ids.Count)
        { violations.Add("'ids' lists an ID twice."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        LayoutRecord record = layout.Read(project.Path);
        LayoutObject[] objects = [.. record.Objects.Where(o => o.Space == space)];
        var targets = new List<LayoutObject>();
        foreach (long id in ids)
        {
            string at = id.ToString(CultureInfo.InvariantCulture);
            LayoutObject? target = objects.FirstOrDefault(o => o.Id == id);
            if (target is null)
            { violations.Add($"{at}: no placed object of space {space} has this ID; read eplan_layout_get."); }
            else if (!Removable(target))
            { violations.Add($"{at}: a {target.Kind} is part of the enclosure; only rails, ducts and devices can be removed."); }
            else
            { targets.Add(target); }
        }

        var chosen = new HashSet<long>(ids);
        foreach (LayoutObject child in objects.Where(o => o.ParentId is { } parent && chosen.Contains(parent) && !chosen.Contains(o.Id)))
        {
            violations.Add($"{child.ParentId!.Value.ToString(CultureInfo.InvariantCulture)}: {child.Tag ?? child.Kind} ({child.Id.ToString(CultureInfo.InvariantCulture)}) is mounted on it; list it too.");
        }

        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        return new LayoutRemoveRequest
        {
            Project = project,
            Space = space,
            Targets = targets,
            State = Fingerprint.Of(objects.Select(o => o.Id.ToString(CultureInfo.InvariantCulture) + "|" + o.ParentId?.ToString(CultureInfo.InvariantCulture))),
        };
    }

    public string StateFingerprint() => Fingerprint.Of([Project.Path, Space, string.Join(",", Targets.Select(t => t.Id)), State]);

    /// <summary>Children before their rail, so nothing is removed from under a device.</summary>
    public List<long> Order() => [.. Targets.OrderBy(t => t.Kind == "Component" ? 0 : 1).Select(t => t.Id)];
}

/// <summary><see cref="OperationNames.LayoutRemovePlan"/>: what eplan_layout_remove would remove; reads only.</summary>
public sealed class LayoutRemovePlanOperation(IProjectCatalog catalog, ILayoutReader layout) : Operation<LayoutRemoveArgs, WritePlan>
{
    public override string Name => OperationNames.LayoutRemovePlan;

    protected override WritePlan Execute(LayoutRemoveArgs args)
    {
        LayoutRemoveRequest request = LayoutRemoveRequest.Check(args, catalog, layout);
        List<PlannedChange> changes = [.. request.Targets.Select(t => new PlannedChange
        {
            Action = "delete",
            Kind = t.Kind,
            Name = t.Tag ?? t.Id.ToString(CultureInfo.InvariantCulture),
            OldValue = string.Join(", ", t.Parts),
        })];
        return new WritePlan { Project = request.Project.Name, ProjectPath = request.Project.Path, Changes = changes, Fingerprint = request.StateFingerprint() };
    }
}

/// <summary>
/// <see cref="OperationNames.LayoutRemoveExecute"/>: checks again, removes the objects in one transaction and reads back:
/// none of the IDs may remain placed and every removed device must be free to place again, otherwise it rolls back.
/// </summary>
public sealed class LayoutRemoveExecuteOperation(IProjectCatalog catalog, ILayoutReader layout, ILayoutRemover remover, ITransactionScopeFactory transactions)
    : Operation<LayoutRemoveArgs, LayoutRemoveData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_layout_remove".</summary>
    public const string Tool = "eplan_layout_remove";

    public override string Name => OperationNames.LayoutRemoveExecute;

    protected override LayoutRemoveData Execute(LayoutRemoveArgs args)
    {
        LayoutRemoveRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one layout.remove.plan returned."); }
            request = LayoutRemoveRequest.Check(args, catalog, layout);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The 3D layout changed after the plan was made, so nothing was changed. Call eplan_layout_remove again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<int> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            remover.Remove(request.Project.Path, request.Order());
            LayoutRecord after = layout.Read(request.Project.Path);
            var left = new HashSet<long>(after.Objects.Select(o => o.Id));
            foreach (LayoutObject target in request.Targets)
            {
                if (left.Contains(target.Id) || (target.Tag is not null && after.Names3D.Contains(target.Tag)))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                        $"{target.Tag ?? target.Kind} ({target.Id.ToString(CultureInfo.InvariantCulture)}) is still in the 3D layout after removing, so nothing was kept.",
                        retryable: false, new Dictionary<string, string> { ["id"] = target.Id.ToString(CultureInfo.InvariantCulture) });
                }
            }

            return request.Targets.Count;
        });
        return new LayoutRemoveData
        {
            Project = request.Project.Name,
            Space = request.Space,
            Removed = [.. request.Targets.Select(t => new LayoutRemoved { Id = t.Id, Kind = t.Kind, Tag = t.Tag, Parts = t.Parts })],
            Warning = committed.Warning,
        };
    }
}
