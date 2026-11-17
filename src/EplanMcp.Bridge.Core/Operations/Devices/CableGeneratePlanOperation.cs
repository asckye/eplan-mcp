using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Plans one project update even with no automatic cables: connections may produce new ones (tool spec 5).</summary>
public sealed class CableGeneratePlanOperation(IProjectCatalog catalog, ICableGenerator generator) : Operation<CableGenerateArgs, WritePlan>
{
    public override string Name => OperationNames.CableGeneratePlan;

    protected override WritePlan Execute(CableGenerateArgs args)
    {
        CableGenerateRequest request = CableGenerateRequest.Check(args, catalog, generator);
        CableGenerationSnapshot snapshot = generator.Read(request.Project.Path);
        OperationContext.Current.ThrowIfCancellationRequested();
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Fingerprint = request.FingerprintOf(snapshot),
            Changes = [new PlannedChange { Action = "update", Kind = "cables", Name = request.Project.Name, Property = "generation" }],
            Details = new Dictionary<string, string>
            {
                ["creationScheme"] = request.Schemes[0],
                ["numberingScheme"] = request.Schemes[1],
                ["selectionScheme"] = request.Schemes[2],
                ["onlyAutoCables"] = "true",
                ["keepOldNames"] = "true",
                ["start"] = "1",
                ["step"] = "1",
                ["before"] = BridgeJson.FromObject(snapshot.Counts()).ToString(),
                ["cables"] = BridgeJson.FromObject(snapshot.Cables.Take(50).Select(c => new { c.Name, c.Automatic })).ToString(),
                ["hint"] = "New cables and selection results are known only after running. Undo after commit has not been verified.",
            },
        };
    }
}
