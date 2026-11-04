using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using ModelContextProtocol;

namespace EplanMcp.Host.Packs;

/// <summary>Stateless v2 writes: confirm without locks, then project/store/binding locks, CAS, audit and atomic publication.</summary>
internal sealed class BindingService(PackLibrary packs, AuditLog audit, ApprovalService approvals, IBridgeClient bridge)
{
    internal Action<string>? Checkpoint { get; init; }
    internal TimeSpan ProjectLockWait { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<ProjectBinding> WriteAsync(ProjectBinding proposed, int expectedRevision, string source, string tool,
        bool dryRun, Func<ProjectBinding, CancellationToken, Task>? validate,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (proposed.BindingVersion != 2)
        {
            throw new PackFormatException("/bindingVersion: explicit bind/migrate must supply a v2 binding.");
        }
        var paths = new PlanPaths(packs.Locations.Root);
        string project = PlanSnapshotHashes.ProjectKey(proposed.Project);
        ProjectBinding next = PackJson.ReadBinding(PackJson.Serialize(proposed with { Project = project, Revision = checked(expectedRevision + 1), Locks = [] }));
        string folder = paths.Check(packs.Workspaces.FolderOf(project));
        string file = paths.Check(Path.Combine(folder, ProjectBinding.FileName));
        byte[] before = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        ProjectBinding? old = before.Length == 0 ? null : PackJson.ReadBinding(before);
        RequireRevision(old);
        var verified = new Dictionary<string, (PackManifest Manifest, PackLock Lock)>(StringComparer.Ordinal);
        try
        {
            next = next with { Locks = await BindingLocks.ResolveAsync(next, VerifyAsync, cancellationToken).ConfigureAwait(false) };
        }
        catch (PackFormatException ex)
        {
            throw Failure(ErrorReason.ConfigConflict, ex.Message);
        }
        byte[] after = PackJson.Serialize(next);
        _ = PackJson.ReadBinding(after);
        if (validate is not null)
        { await validate(next, cancellationToken).ConfigureAwait(false); }
        bool unchanged = old is not null && PackJson.Serialize(old).AsSpan().SequenceEqual(PackJson.Serialize(next with { Revision = old.Revision }));
        if (dryRun || unchanged)
        { return unchanged ? old! : next; }
        string beforeDigest = PlanStore.Hash(before), afterDigest = PlanStore.Hash(after);
        string requestId = Guid.NewGuid().ToString("N");
        ApprovalResult? decision = null;
        if (!(old?.Locks ?? []).OrderBy(p => p.Id, StringComparer.Ordinal).SequenceEqual(next.Locks))
        {
            EplanInstanceInfo? eplan = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake?.Eplan;
            if (eplan is null)
            {
                throw Failure(ErrorReason.ApprovalDenied, "Changing project pack locks needs the engineer's confirmation; connect EPLAN and try again.");
            }
            var plan = new WritePlan
            {
                ProjectPath = project,
                Fingerprint = afterDigest,
                Changes = [.. (old?.Locks ?? []).Concat(next.Locks).Select(p => p.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                    .Select(id => new PlannedChange { Action = "update", Kind = "binding", Name = id, Property = "pack locks", OldValue = Pin(old?.Locks, id), NewValue = Pin(next.Locks, id) })],
            };
            decision = await approvals.AskAsync(requestId, tool, ToolTier.Write, PlanStore.Hash(PackJson.Serialize(new { beforeDigest, afterDigest, source })), plan, eplan, null, null, progress, cancellationToken).ConfigureAwait(false);
            decision.Outcome?.Dispose();
            if (decision.Decision != ApprovalDecision.Granted)
            {
                await RecordAsync("denied", false, cancellationToken).ConfigureAwait(false);
                throw new BridgeCallException(approvals.Refusal(decision.Decision));
            }
        }
        Checkpoint?.Invoke("locking");
        using FileStream projectLock = await FileLock.AcquireAsync(paths.ProjectLock(project), ProjectLockWait, cancellationToken).ConfigureAwait(false)
            ?? throw new PlanCapacityException("plans.project", 1, 1);
        using FileStream storeLock = await FileLock.AcquireAsync(paths.Check(Path.Combine(packs.Installed.Root, PackStore.LockFileName)), packs.Installed.LockWait, cancellationToken).ConfigureAwait(false)
            ?? throw Failure(ErrorReason.Timeout, "The pack store is busy; bind again when the other change finishes.");
        _ = paths.DirectoryAt(folder);
        using FileStream bindingLock = await FileLock.AcquireAsync(paths.Check(Path.Combine(folder, ".binding.lock")), packs.Workspaces.LockWait, cancellationToken).ConfigureAwait(false)
            ?? throw Failure(ErrorReason.Timeout, "The binding is busy; read it again when the other change finishes.");
        byte[] current = await ReadAsync(file, cancellationToken).ConfigureAwait(false);
        RequireRevision(current.Length == 0 ? null : PackJson.ReadBinding(current));
        if (!before.AsSpan().SequenceEqual(current))
        {
            throw Failure(ErrorReason.PlanStale, "/revision: the binding changed during confirmation; read it and bind again.");
        }
        foreach (var pack in verified.Values.ToArray())
        {
            _ = await VerifyAsync(pack.Manifest.Id, pack.Manifest.Version, cancellationToken).ConfigureAwait(false);
        }
        string partial = paths.Check(file + $".partial-{Guid.NewGuid():N}");
        var storage = new PlanStore(packs.Locations, audit) { Checkpoint = _ => Checkpoint?.Invoke("written") };
        try
        {
            await storage.WriteAsync(partial, after, cancellationToken).ConfigureAwait(false);
            Checkpoint?.Invoke("staged");
            await RecordAsync("started", false, cancellationToken).ConfigureAwait(false);
            if (old?.BindingVersion == 1)
            {
                File.Copy(file, paths.Check(Path.Combine(folder, $"binding.v1-{beforeDigest}.json")), overwrite: false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await storage.ReplaceAsync(partial, file, cancellationToken).ConfigureAwait(false);
            await RecordAsync("committed", true, CancellationToken.None).ConfigureAwait(false);
            return next;
        }
        finally
        {
            File.Delete(partial);
        }
        void RequireRevision(ProjectBinding? binding)
        {
            if ((binding?.Revision ?? 0) != expectedRevision || binding is not null && !string.Equals(binding.Project, project, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(ErrorReason.PlanStale, "/revision: expected revision differs; read the binding and bind again.");
            }
        }
        async Task<(PackManifest, PackLock)> VerifyAsync(string id, string version, CancellationToken token)
        {
            PackCheckResult check = await PackVerifier.CheckDirectoryAsync(paths.Check(packs.Installed.FolderOf(id, version)), token).ConfigureAwait(false);
            if (!check.Ok || check.Manifest is not { } manifest || check.Lock is not { } frozen)
            {
                throw Failure(Directory.Exists(packs.Installed.FolderOf(id, version)) ? ErrorReason.PackInvalid : ErrorReason.ResourceNotFound, "/locks: an installed pack or its exact lock is unavailable or invalid; check the pack before binding.");
            }
            if (verified.TryGetValue(id, out var earlier) && (earlier.Lock.Pack != frozen.Pack || !PackJson.Serialize(earlier.Lock).AsSpan().SequenceEqual(PackJson.Serialize(frozen))))
            {
                throw Failure(ErrorReason.PlanStale, "/locks: installed content changed during confirmation; bind again.");
            }
            verified[id] = (manifest, frozen);
            return (manifest, frozen);
        }

        async Task RecordAsync(string phase, bool changed, CancellationToken token)
        {
            try
            {
                await audit.AppendAsync(new JsonObject
                {
                    ["kind"] = "workspace_change",
                    ["requestId"] = requestId,
                    ["tool"] = tool,
                    ["project"] = project,
                    ["phase"] = phase,
                    ["beforeDigest"] = beforeDigest,
                    ["afterDigest"] = afterDigest,
                    ["source"] = source,
                    ["revision"] = next.Revision,
                    ["channel"] = decision?.Channel,
                    ["decision"] = decision?.Decision.ToString()
                }, token).ConfigureAwait(false);
            }
            catch (AuditUnavailableException)
            {
                throw Failure(ErrorReason.AuditUnavailable, changed ? "The binding changed, but its final audit record is unavailable; run 'eplanmcp audit verify'." : "The audit log is unavailable; nothing changed. Run 'eplanmcp doctor'.");
            }
        }
    }

    public Task<ProjectBinding> WriteAsync(ProjectBinding proposed, int expectedRevision, string source, string tool,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken) => WriteAsync(proposed, expectedRevision, source, tool, false, null, progress, cancellationToken);

    private static async Task<byte[]> ReadAsync(string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        { return []; }
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > PackJson.MaxFileBytes)
        { throw new PackFormatException("/binding: exceeds 16 MiB."); }
        byte[] bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static BridgeCallException Failure(string reason, string message) => new(new BridgeError { Reason = reason, Message = message });
    private static string? Pin(IReadOnlyList<PackReference>? pins, string id) => pins?.FirstOrDefault(p => p.Id == id) is { } pin ? $"{pin.Version} sha256:{pin.Digest}" : null;
}
