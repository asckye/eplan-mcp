using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartSyncPlan"/>: classifies the project's parts against the configured parts database
/// (docs/tools/eplan_part_sync.md, section 5). The plan names the project and the database, so the Host asks every time
/// and backs both up first (ADR-0013, ADR-0019). One change each for the parts added to and overwritten in the
/// database, with the same kind and details as part.import.plan, and one for the referenced parts the project will
/// store (S24 r15). A project whose synchronization changes nothing plans no change. Reads only.
/// </summary>
public sealed class PartSyncPlanOperation(IProjectCatalog catalog, IPartReader reader, IPartsDatabaseWriter writer) : Operation<PartSyncArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> keys beyond part.import.plan's.</summary>
    public const string Stored = "stored";
    public const string StoredCount = "storedCount";
    public const string ProjectPartsBefore = "projectPartsBefore";

    /// <summary>The <see cref="PlannedChange.Property"/> of the change that stores parts in the project.</summary>
    public const string StoredParts = "stored parts";

    public override string Name => OperationNames.PartSyncPlan;

    protected override WritePlan Execute(PartSyncArgs args)
    {
        PartSyncRequest request = PartSyncRequest.Check(args, catalog, reader, writer);
        string project = request.Project.Name;
        var changes = new List<PlannedChange>();
        if (request.Added.Count > 0)
        {
            changes.Add(new PlannedChange { Action = "create", Kind = PartImportPlanOperation.Parts, Name = project, Property = PartImportPlanOperation.Parts, NewValue = PartImportRequest.Number(request.Added.Count) });
        }

        if (request.Overwritten.Count > 0)
        {
            changes.Add(new PlannedChange { Action = "update", Kind = PartImportPlanOperation.Parts, Name = project, Property = PartImportPlanOperation.Parts, NewValue = PartImportRequest.Number(request.Overwritten.Count) });
        }

        if (request.StoredInProject.Count > 0)
        {
            changes.Add(new PlannedChange { Action = "update", Kind = "project", Name = project, Property = StoredParts, OldValue = PartImportRequest.Number(request.ProjectPartCount), NewValue = PartImportRequest.Number(request.StoredInProject.Count) });
        }

        int skipped = request.Stored.Count - request.Added.Count - request.Overwritten.Count;
        return new WritePlan
        {
            Project = project,
            ProjectPath = request.Project.Path,
            Changes = changes,
            Fingerprint = request.StateFingerprint(),
            MasterData = request.Target(),
            Details = new Dictionary<string, string>
            {
                [PartImportPlanOperation.Added] = PartImportRequest.Lines(request.Added),
                [PartImportPlanOperation.AddedCount] = PartImportRequest.Number(request.Added.Count),
                [PartImportPlanOperation.Updated] = PartImportRequest.Lines(request.Overwritten),
                [PartImportPlanOperation.UpdatedCount] = PartImportRequest.Number(request.Overwritten.Count),
                [PartImportPlanOperation.SkippedCount] = PartImportRequest.Number(skipped),
                [Stored] = PartImportRequest.Lines(request.StoredInProject),
                [StoredCount] = PartImportRequest.Number(request.StoredInProject.Count),
                [PartImportPlanOperation.PartsBefore] = PartImportRequest.Number(request.PartCount),
                [ProjectPartsBefore] = PartImportRequest.Number(request.ProjectPartCount),
                [PartImportPlanOperation.Mode] = request.Mode,
            },
        };
    }
}
