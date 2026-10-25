using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Audit;

namespace EplanMcp.Host.Plans;

/// <summary>Cross-process mutations hold the store lock, then the plan lock; readers open immutable snapshots sharing delete.</summary>
internal sealed partial class PlanStore(HostDataLocations locations, AuditLog audit)
{
    private readonly PlanPaths _paths = new(locations.Root);
    internal Action<string>? Checkpoint { get; init; }

    public async Task<PlanRecord> PublishAsync(PlanDefinition definition, string changes, IReadOnlyDictionary<string, byte[]> evidence, CancellationToken cancellationToken)
    {
        byte[] text = new UTF8Encoding(false, true).GetBytes(changes);
        if (definition.Digests.ChangesDigest != Hash(text))
        {
            throw new ArgumentException("The changes digest does not match the complete text.", nameof(changes));
        }
        var record = new PlanRecord(1, PlanCanonical.Hash(definition), definition, new());
        PlanStorageRules.Validate(record, definition.PlanId);
        byte[] json = Encode(record);
        Dictionary<string, byte[]> copies = evidence.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);
        foreach ((string hash, byte[] bytes) in copies)
        {
            if (!PlanPaths.IsHex(hash, 64) || hash != Hash(bytes))
            {
                throw new ArgumentException("Evidence must be addressed by its SHA-256.", nameof(evidence));
            }
            using JsonDocument parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
            _ = PlanCanonical.Serialize(parsed.RootElement);
        }
        using FileStream store = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        BeforeCreate(definition, json.LongLength + text.LongLength + copies.Values.Sum(b => b.LongLength));
        string file = _paths.Record(definition.PlanId);
        string assets = _paths.Assets(definition.PlanId);
        if (File.Exists(file) || Directory.Exists(assets))
        {
            throw new IOException("A plan ID cannot be published twice.");
        }
        string partial = _paths.DirectoryAt(Path.Combine(_paths.Root, $".partial-{Guid.NewGuid():N}"));
        string stagedAssets = _paths.DirectoryAt(Path.Combine(partial, definition.PlanId));
        string stagedEvidence = _paths.DirectoryAt(Path.Combine(stagedAssets, "evidence"));
        foreach ((string hash, byte[] bytes) in copies)
        {
            await WriteAsync(Path.Combine(stagedEvidence, hash + ".json"), bytes, cancellationToken).ConfigureAwait(false);
        }
        await WriteAsync(Path.Combine(stagedAssets, "changes.txt"), text, cancellationToken).ConfigureAwait(false);
        string stagedRecord = Path.Combine(partial, "record.json");
        await WriteAsync(stagedRecord, json, cancellationToken).ConfigureAwait(false);
        Checkpoint?.Invoke("staged");
        await audit.AppendAsync(new JsonObject
        {
            ["kind"] = "plan_compiled",
            ["planId"] = definition.PlanId,
            ["planHash"] = record.PlanHash,
            ["owner"] = JsonSerializer.SerializeToNode(definition.Owner, PlanJson.Options),
            ["sources"] = JsonSerializer.SerializeToNode(definition.Sources, PlanJson.Options),
        }, cancellationToken).ConfigureAwait(false);
        Checkpoint?.Invoke("audited");
        Directory.Move(_paths.Check(stagedAssets), _paths.Check(assets));
        Checkpoint?.Invoke("evidence_published");
        File.Move(_paths.Check(stagedRecord), _paths.Check(file)); // No overwrite: this is the visible commit point.
        Checkpoint?.Invoke("published");
        Directory.Delete(_paths.Check(partial));
        return record;
    }

    public async Task<PlanRecord?> ReadAsync(string planId, CancellationToken cancellationToken)
    {
        string file = _paths.Record(planId);
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            string json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument parsed = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (parsed.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new NotSupportedException("Unknown plan schema; keep the record and refuse execution.");
            }
            PlanRecord record = PlanJson.Read<PlanRecord>(json);
            PlanStorageRules.Validate(record, planId);
            _ = _paths.Files(_paths.Assets(planId)).Count();
            return record;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>Null means a revision conflict; no callback is run and no bytes are changed in that case.</summary>
    public async Task<PlanRecord?> UpdateAsync(string planId, long revision, Func<PlanState, PlanState> change, CancellationToken cancellationToken)
    {
        PlanPaths.Id(planId);
        using FileStream store = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        using FileStream held = await LockAsync(planId + ".lock", cancellationToken).ConfigureAwait(false);
        PlanRecord before = await ReadAsync(planId, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The plan does not exist.");
        if (before.State.Revision != revision)
        {
            return null;
        }
        PlanRecord after = before with { State = change(before.State) };
        PlanStorageRules.Transition(before.State, after.State);
        PlanStorageRules.Validate(after, planId);
        byte[] json = Encode(after);
        BeforeReplace(planId, json.LongLength);
        string file = _paths.Record(planId);
        string temporary = file + $".partial-{Guid.NewGuid():N}";
        await WriteAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        Checkpoint?.Invoke("replacement_staged");
        await ReplaceAsync(temporary, file, cancellationToken).ConfigureAwait(false);
        Checkpoint?.Invoke("replaced");
        return after;
    }

    public async Task<byte[]> ReadEvidenceAsync(string planId, string sha256, CancellationToken cancellationToken)
    {
        if (!PlanPaths.IsHex(sha256, 64))
        {
            throw new ArgumentException("An evidence hash must be lowercase SHA-256.", nameof(sha256));
        }
        byte[] bytes = await File.ReadAllBytesAsync(_paths.Check(Path.Combine(_paths.Assets(planId), "evidence", sha256 + ".json")), cancellationToken).ConfigureAwait(false);
        if (Hash(bytes) != sha256)
        {
            throw new JsonException("Evidence has changed.");
        }
        return bytes;
    }

    public string ChangesPath(string planId) => _paths.Check(Path.Combine(_paths.Assets(planId), "changes.txt"));

    public async Task<string> ReadChangesAsync(string planId, CancellationToken cancellationToken)
    {
        PlanRecord record = await ReadAsync(planId, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The plan does not exist.");
        byte[] bytes = await File.ReadAllBytesAsync(_paths.Check(Path.Combine(_paths.Assets(planId), "changes.txt")), cancellationToken).ConfigureAwait(false);
        if (Hash(bytes) != record.Definition.Digests.ChangesDigest)
        {
            throw new JsonException("The complete changes text has changed.");
        }
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    public async Task<string> PutEvidenceAsync(string planId, byte[] evidence, CancellationToken cancellationToken)
    {
        byte[] bytes = evidence.ToArray();
        using JsonDocument parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
        _ = PlanCanonical.Serialize(parsed.RootElement);
        string hash = Hash(bytes);
        PlanPaths.Id(planId);
        using FileStream store = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        using FileStream held = await LockAsync(planId + ".lock", cancellationToken).ConfigureAwait(false);
        _ = await ReadAsync(planId, cancellationToken).ConfigureAwait(false) ?? throw new FileNotFoundException("The plan does not exist.");
        string directory = _paths.Check(Path.Combine(_paths.Assets(planId), "evidence"));
        string file = _paths.Check(Path.Combine(directory, hash + ".json"));
        if (File.Exists(file))
        {
            _ = await ReadEvidenceAsync(planId, hash, cancellationToken).ConfigureAwait(false);
            return hash;
        }
        BeforeEvidence(planId, bytes.LongLength);
        string temporary = Path.Combine(directory, $".partial-{Guid.NewGuid():N}");
        await WriteAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
        Checkpoint?.Invoke("evidence_staged");
        File.Move(_paths.Check(temporary), _paths.Check(file));
        return hash;
    }

    private async Task<FileStream> LockAsync(string name, CancellationToken cancellationToken)
    {
        _paths.DirectoryAt(_paths.Root);
        long deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_paths.Check(Path.Combine(_paths.Root, name)), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) && Environment.TickCount64 < deadline)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteAsync(string file, byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(_paths.Check(file), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
        Checkpoint?.Invoke("flushed");
    }

    private async Task ReplaceAsync(string temporary, string file, CancellationToken cancellationToken)
    {
        long deadline = Environment.TickCount64 + 5000;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(_paths.Check(temporary), _paths.Check(file), overwrite: true);
                return; // Same-volume rename; never delete the old record before replacement.
            }
            catch (Exception ex) when (Environment.TickCount64 < deadline && (ex is UnauthorizedAccessException
                || ex is IOException { HResult: unchecked((int)0x80070020) }))
            {
                // Windows may deny replacing an open destination even when its readers share delete.
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static byte[] Encode(PlanRecord record) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, PlanJson.Options));
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    partial void BeforeCreate(PlanDefinition definition, long bytes);
    partial void BeforeReplace(string planId, long bytes);
    partial void BeforeEvidence(string planId, long bytes);
}
