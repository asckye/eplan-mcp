using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only import.list plan: one project update, without predicting row-level effects (tool spec 5).</summary>
public sealed class ImportListPlanOperation(IProjectCatalog catalog, IListImporter importer) : Operation<ImportListArgs, WritePlan>
{
    private Action<ImportSource> ReadSource { get; } = source => source.Read(hash: true, maxBytes: 100 * 1024 * 1024);

    internal ImportListPlanOperation(IProjectCatalog catalog, IListImporter importer, Action<ImportSource> read) : this(catalog, importer) => ReadSource = read;

    public override string Name => OperationNames.ImportListPlan;

    protected override WritePlan Execute(ImportListArgs args)
    {
        ImportListRequest request = ImportListRequest.Check(args, catalog, importer, ReadSource);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new PlannedChange
            {
                Action = "update", Kind = request.Kind, Name = request.Project.Name, Property = "list",
                OldValue = request.Source.Describe() + "; added, updated and overwritten rows can only be checked after running",
            }],
            Fingerprint = request.StateFingerprint(),
            Details = request.Details(),
        };
    }
}
