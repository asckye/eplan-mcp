using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The master data a project stores, implemented by the EPLAN adapter layer. Main thread only.</summary>
public interface IProjectMasterData
{
    /// <summary>The file names of the master data stored in the project.</summary>
    public IReadOnlyList<string> Entries(string projectPath);

    /// <summary>Replaces what the project stores with the system's master data. System master data is only read.</summary>
    public void Update(string projectPath);
}

/// <summary>The checked arguments of eplan_masterdata_update and what the project stores.</summary>
internal sealed class MasterDataUpdateRequest
{
    private MasterDataUpdateRequest(OpenProject project, List<string> entries)
    {
        Project = project;
        Entries = entries;
    }

    public OpenProject Project { get; }

    public List<string> Entries { get; }

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(Entries));

    public static MasterDataUpdateRequest Check(MasterDataUpdateArgs args, IProjectCatalog catalog, IProjectMasterData masterData)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new MasterDataUpdateRequest(project, [.. masterData.Entries(project.Path).OrderBy(e => e, StringComparer.Ordinal)]);
    }
}

/// <summary><see cref="OperationNames.MasterDataUpdatePlan"/>: the scope, since only EPLAN can tell which files differ from the system's. Reads only.</summary>
public sealed class MasterDataUpdatePlanOperation(IProjectCatalog catalog, IProjectMasterData masterData) : Operation<MasterDataUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.MasterDataUpdatePlan;

    protected override WritePlan Execute(MasterDataUpdateArgs args)
    {
        MasterDataUpdateRequest request = MasterDataUpdateRequest.Check(args, catalog, masterData);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new PlannedChange { Action = "update", Kind = "project", Name = request.Project.Name, Property = "master data", OldValue = FormattableString.Invariant($"{request.Entries.Count} stored files"), NewValue = "the system's versions" }],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.MasterDataUpdateExecute"/>: checks the plan's fingerprint again, backs the project up
/// (<see cref="ProjectBackups"/>) and lets EPLAN update the stored files. Outside a transaction: nothing shows that
/// EPLAN's rollback or undo covers them, so a failure is OUTCOME_UNKNOWN and the backup is the way back.
/// </summary>
public sealed class MasterDataUpdateExecuteOperation(IProjectCatalog catalog, IProjectMasterData masterData, IPageReader pages, IProjectBackup backup)
    : Operation<MasterDataUpdateArgs, MasterDataUpdateData>, IWriteOperation
{
    public override string Name => OperationNames.MasterDataUpdateExecute;

    protected override MasterDataUpdateData Execute(MasterDataUpdateArgs args)
    {
        MasterDataUpdateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one masterdata.update.plan returned."); }
            request = MasterDataUpdateRequest.Check(args, catalog, masterData);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's master data changed after the plan was made, so nothing was updated. Call eplan_masterdata_update again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string path = request.Project.Path;
        string copy = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        masterData.Update(path);
        IReadOnlyList<string> after = masterData.Entries(path);
        return new MasterDataUpdateData
        {
            Project = request.Project.Name,
            Entries = after.Count,
            Added = [.. after.Except(request.Entries, StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal)],
            Backup = copy,
        };
    }
}
