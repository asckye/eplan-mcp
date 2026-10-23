using System.Text;
using System.Text.Json;

namespace EplanMcp.Host.Plans;

internal sealed partial class PlanStore
{
    internal TimeProvider Clock { get; init; } = TimeProvider.System;
    internal int LifetimeHours { get; init; } = 24;
    public DateTimeOffset ExpiresAt(DateTimeOffset createdAt) => createdAt.AddHours(LifetimeHours);

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using FileStream held = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        Cleanup();
    }

    partial void BeforeCreate(PlanDefinition definition, long bytes)
    {
        Cleanup();
        string quarantine = _paths.Check(Path.Combine(_paths.Root, "quarantine"));
        if (Directory.Exists(quarantine) && Directory.EnumerateDirectories(quarantine, definition.PlanId + "-*").Any())
        {
            throw new InvalidOperationException("A quarantined plan ID cannot be published again; compile a new plan ID.");
        }
        if (LifetimeHours is < 1 or > 24 || definition.ExpiresAt > ExpiresAt(definition.CreatedAt))
        {
            throw new ArgumentException("A plan's lifetime may only shorten the configured 1-24 hour limit.", nameof(definition));
        }
        long recordBytes = Encode(new PlanRecord(1, PlanCanonical.Hash(definition), definition, new())).LongLength;
        PlanCapacity.Parts(definition, recordBytes, bytes - recordBytes);
        (int count, long used, _) = Usage();
        PlanCapacity.Require("plans.records", count, 1, PlanCapacity.Records);
        // A record that can still run keeps its full reservation; terminal and expired ones count their actual bytes only.
        DateTimeOffset now = Clock.GetUtcNow();
        long reserved = RecordFiles().Where(f => PlanCapacity.CanGrow(ReadKnown(f), now))
            .Sum(f => Math.Max(0, PlanCapacity.PerPlan - PlanBytes(Path.GetFileNameWithoutExtension(f))));
        PlanCapacity.Require("plans.bytes", used + reserved, PlanCapacity.PerPlan, PlanCapacity.Total);
    }

    partial void BeforeReplace(string planId, long bytes)
    {
        PlanRecord record = ReadKnown(_paths.Record(planId));
        PlanCapacity.Parts(record.Definition, bytes, PlanBytes(planId) - new FileInfo(_paths.Record(planId)).Length);
        PlanCapacity.Require("plans.bytes", Usage().Bytes, bytes, PlanCapacity.Total); // Includes the temporary replacement.
    }

    partial void BeforeEvidence(string planId, long bytes)
    {
        string file = _paths.Record(planId);
        long recordBytes = new FileInfo(file).Length;
        PlanCapacity.Parts(ReadKnown(file).Definition, recordBytes, PlanBytes(planId) - recordBytes + bytes);
        PlanCapacity.Require("plans.bytes", Usage().Bytes, bytes, PlanCapacity.Total);
    }

    public async Task<(int Count, long Bytes, int Quarantined)> UsageAsync(CancellationToken cancellationToken)
    {
        using FileStream held = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        return Usage();
    }

    internal (int Count, long Bytes, int Quarantined) Usage()
    {
        long bytes = _paths.Files(_paths.Root).Sum(f => new FileInfo(f).Length);
        string quarantine = _paths.Check(Path.Combine(_paths.Root, "quarantine"));
        int quarantined = Directory.Exists(quarantine) ? Directory.GetDirectories(quarantine).Length : 0;
        return (RecordFiles().Count() + quarantined, bytes, quarantined);
    }

    private IEnumerable<string> RecordFiles() => Directory.Exists(_paths.Root) ? Directory.EnumerateFiles(_paths.Root, "*.json") : [];
    private long PlanBytes(string id) => new FileInfo(_paths.Record(id)).Length + _paths.Files(_paths.Assets(id)).Sum(f => new FileInfo(f).Length);

    private static PlanRecord ReadKnown(string file)
    {
        PlanRecord record = PlanJson.Read<PlanRecord>(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file)));
        PlanStorageRules.Validate(record, Path.GetFileNameWithoutExtension(file));
        return record;
    }

    private void Cleanup()
    {
        _ = _paths.Files(_paths.Root).Count(); // Validate the entire tree before any move or recursive deletion.
        foreach (string file in RecordFiles().ToArray())
        {
            PlanRecord record;
            try
            { record = ReadKnown(file); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                string id = Path.GetFileNameWithoutExtension(file);
                string destination = _paths.DirectoryAt(Path.Combine(_paths.Root, "quarantine", (PlanPaths.IsHex(id, 32) ? id : "invalid") + "-" + Guid.NewGuid().ToString("N")));
                File.Move(_paths.Check(file), _paths.Check(Path.Combine(destination, "record.json")));
                if (PlanPaths.IsHex(id, 32) && Directory.Exists(_paths.Assets(id)))
                {
                    Directory.Move(_paths.Assets(id), _paths.Check(Path.Combine(destination, id)));
                }
                continue; // Quarantine is permanent: no repair, expiry or execution path.
            }
            if (!PlanCapacity.Retained(record, Clock.GetUtcNow()))
            {
                using FileStream held = new(_paths.Check(Path.Combine(_paths.Root, record.Definition.PlanId + ".lock")), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                File.Delete(_paths.Check(file));
                string assets = _paths.Assets(record.Definition.PlanId);
                if (Directory.Exists(assets))
                {
                    Directory.Delete(_paths.Check(assets), recursive: true);
                }
            }
        }
    }
}
