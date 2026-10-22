using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PartImportPlan"/>: reads the file's part numbers and classifies them against the configured
/// parts database (docs/tools/eplan_part_import.md, section 5). The plan has no project; it names the database
/// (<see cref="WritePlan.MasterData"/>), so the Host asks the engineer every time and backs the database up first
/// (ADR-0019). One change for the parts added and one for the parts updated, each named by the file and with its count;
/// the file with its size and SHA-256 and the part numbers, at most 50 per kind, are in the details. A file whose parts
/// the mode all skips plans no change. Reads only.
/// </summary>
public sealed class PartImportPlanOperation(IPartsDatabaseWriter writer) : Operation<PartImportArgs, WritePlan>
{
    /// <summary>The <see cref="WritePlan.Details"/> keys.</summary>
    public const string File = "file";
    public const string Added = "added";
    public const string AddedCount = "addedCount";
    public const string Updated = "updated";
    public const string UpdatedCount = "updatedCount";
    public const string Skipped = "skipped";
    public const string SkippedCount = "skippedCount";
    public const string PartsBefore = "partsBefore";
    public const string Mode = "mode";
    public const string MatchBy = "matchBy";

    /// <summary>The <see cref="PlannedChange.Kind"/> and <see cref="PlannedChange.Property"/> of the changes.</summary>
    public const string Parts = "parts";

    public override string Name => OperationNames.PartImportPlan;

    protected override WritePlan Execute(PartImportArgs args)
    {
        PartImportRequest request = PartImportRequest.Check(args, writer);
        string file = request.Source.Describe();
        var changes = new List<PlannedChange>();
        if (request.Added.Count > 0)
        {
            changes.Add(new PlannedChange { Action = "create", Kind = Parts, Name = request.Source.File, Property = Parts, NewValue = PartImportRequest.Number(request.Added.Count) });
        }

        if (request.Updated.Count > 0)
        {
            changes.Add(new PlannedChange { Action = "update", Kind = Parts, Name = request.Source.File, Property = Parts, NewValue = PartImportRequest.Number(request.Updated.Count) });
        }

        return new WritePlan
        {
            Changes = changes,
            Fingerprint = request.StateFingerprint(),
            MasterData = request.Target(),
            Details = new Dictionary<string, string>
            {
                [File] = file,
                [Added] = PartImportRequest.Lines(request.Added),
                [AddedCount] = PartImportRequest.Number(request.Added.Count),
                [Updated] = PartImportRequest.Lines(request.Updated),
                [UpdatedCount] = PartImportRequest.Number(request.Updated.Count),
                [Skipped] = PartImportRequest.Lines(request.Skipped),
                [SkippedCount] = PartImportRequest.Number(request.Skipped.Count),
                [PartsBefore] = PartImportRequest.Number(request.PartCount),
                [Mode] = request.Mode,
                [MatchBy] = request.MatchBy,
            },
        };
    }
}
