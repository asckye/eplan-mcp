using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks state on both sides of the backup, then guards readback before committing (tool spec 5; S34).</summary>
public sealed class CableGenerateExecuteOperation(IProjectCatalog catalog, IPageReader pages, ICableGenerator generator, IProjectBackup backup, ITransactionScopeFactory transactions, ISystemMessageReader? messages = null)
    : Operation<CableGenerateArgs, CableGenerateData>, IWriteOperation
{
    private const string MessageWarning = "EPLAN messages could not be read; inspect the session messages in EPLAN.";

    // Keep backup filesystem checks in ProjectBackups; Small tests replace only this boundary.
    internal Action<OpenProject, BackupTarget?> MakeBackup { get; set; } = (project, target) => ProjectBackups.Make(backup, pages, project, target);

    public override string Name => OperationNames.CableGenerateExecute;

    protected override CableGenerateData Execute(CableGenerateArgs args)
    {
        int bookmark = -1;
        string? messageWarning = null;
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
            // After the backup: opening and closing the copy logs messages with local paths that are not this call's.
            try
            {
                bookmark = messages?.ReadAll().Count ?? 0;
            }
            catch (Exception)
            {
                messageWarning = MessageWarning;
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<CableGenerateData> committed;
        try
        {
            committed = TransactionRunner.Run(transactions, "eplan_cable_generate", () => Generate(request, before));
        }
        catch (BridgeErrorException ex)
        {
            var failed = new CableGenerateData { Warning = messageWarning };
            CollectMessages(failed, bookmark);
            ex.Error.Metadata ??= [];
            ex.Error.Metadata["eplanMessages"] = Protocol.BridgeJson.FromObject(failed.EplanMessages).ToString(Newtonsoft.Json.Formatting.None);
            ex.Error.Metadata["eplanMessageCount"] = failed.EplanMessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (failed.Warning is not null)
            {
                ex.Error.Metadata["warning"] = failed.Warning;
            }

            throw;
        }

        committed.Result.Warning = committed.Warning;
        if (messageWarning is not null)
        {
            committed.Result.Warning += " " + messageWarning;
        }

        CollectMessages(committed.Result, bookmark);
        return committed.Result;
    }

    private void CollectMessages(CableGenerateData result, int bookmark)
    {
        if (bookmark < 0)
        {
            return;
        }

        try
        {
            List<SystemMessageRecord> added = [.. (messages?.ReadAll() ?? []).Skip(bookmark).Where(m => MessageListOperation.LevelName(m.Level) is not null)];
            result.EplanMessageCount = added.Count;
            result.EplanMessages = [.. added.Take(20).Select(m => new SystemMessage
            {
                Level = MessageListOperation.LevelName(m.Level)!, Number = m.Index.Length == 0 || m.Index == MessageListOperation.NoNumber ? null : m.Index,
                Text = m.Text, Occurrences = m.Occurrences,
            })];
        }
        catch (Exception)
        {
            // Message collection must not replace the known execution outcome.
            result.Warning += " " + MessageWarning;
        }
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
