using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks state on both sides of the backup, then guards readback before committing (tool spec 5; S34).</summary>
public sealed class CableGenerateExecuteOperation(IProjectCatalog catalog, IPageReader pages, ICableGenerator generator, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<CableGenerateArgs, CableGenerateData>, IWriteOperation
{
    // Keep backup filesystem checks in ProjectBackups; Small tests replace only this boundary.
    internal Action<OpenProject, BackupTarget?> MakeBackup { get; set; } = (project, target) => ProjectBackups.Make(backup, pages, project, target);

    public override string Name => OperationNames.CableGenerateExecute;

    protected override CableGenerateData Execute(CableGenerateArgs args)
    {
        CableGenerateRequest request;
        CableGenerationSnapshot before;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            {
                throw InvalidArgument("'fingerprint' is required: the one cable.generate.plan returned.");
            }

            request = CableGenerateRequest.Check(args, catalog, generator);
            before = generator.Read(request.Project.Path);
            request.RequireCurrent(before, args.Fingerprint);
            MakeBackup(request.Project, args.Backup);
            request.RequireCurrent(generator.Read(request.Project.Path), args.Fingerprint);
            OperationContext.Current.ThrowIfCancellationRequested();
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<CableGenerateData> committed = TransactionRunner.Run(transactions, "eplan_cable_generate", () => Generate(request, before));
        committed.Result.Warning = committed.Warning;
        return committed.Result;
    }

    private CableGenerateData Generate(CableGenerateRequest request, CableGenerationSnapshot before)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        generator.Generate(request.Project.Path, request.Schemes[0], request.Schemes[1], request.Schemes[2]);
        OperationContext.Current.ThrowIfCancellationRequested();
        CableGenerationSnapshot after = generator.Read(request.Project.Path);
        var old = before.Cables.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var current = after.Cables.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (CableGenerationCable cable in before.Cables)
        {
            if (!current.TryGetValue(cable.Id, out CableGenerationCable now) || cable.Name != now.Name || (cable.Automatic != true && (cable.Automatic != now.Automatic || cable.Business != now.Business)))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable readback violated the protected scope. Ask the engineer to check the schemes.", retryable: false,
                    new Dictionary<string, string> { ["before"] = cable.Name, ["after"] = now?.Name ?? "removed", ["change"] = now is null ? "removed" : cable.Name != now.Name ? "renamed" : "protected cable changed" });
            }
        }

        if (after.Cables.Any(c => c.Automatic != true && (!old.TryGetValue(c.Id, out CableGenerationCable previous) || previous.Automatic == true)))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "A generated cable's automatic flag is not true. Ask the engineer to check the schemes.");
        }

        List<CableGenerateChange> changes = [.. after.Cables.Where(c => !old.TryGetValue(c.Id, out CableGenerationCable previous) || previous.Business != c.Business || previous.Automatic != c.Automatic)
            .Select(c => new CableGenerateChange { Kind = old.ContainsKey(c.Id) ? "updated" : "created", Name = c.Name })];
        return new CableGenerateData
        {
            Project = request.Project.Name,
            Before = before.Counts(),
            After = after.Counts(),
            Changes = [.. changes.Take(200)],
            ChangedCount = changes.Count,
            Truncated = changes.Count > 200,
            Hint = "Readback covers cable names, flags, type, wire count, cross-section, length, parts and core connection ends. Full selection effects and undo require Large verification. Inspect omitted changes with eplan_device_list (category Cable) or eplan_cable_get; do not repeat generation.",
        };
    }
}
