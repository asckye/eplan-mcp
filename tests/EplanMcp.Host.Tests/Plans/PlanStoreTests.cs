using System.Text;
using System.Text.Json;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publish_RoundTripEvidenceChangesAndAudit_RefusesOverwrite()
    {
        using var f = new PlanStoreFixture();
        byte[] evidence = Encoding.UTF8.GetBytes("{\"pages\":[]}");
        string hash = PlanStore.Hash(evidence);
        PlanRecord published = await f.Store.PublishAsync(f.Definition, PlanStoreFixture.Changes, new Dictionary<string, byte[]> { [hash] = evidence }, Token);
        PlanRecord read = Assert.IsType<PlanRecord>(await f.Store.ReadAsync(f.Definition.PlanId, Token));
        Assert.Equal(published.PlanHash, read.PlanHash);
        Assert.Equal(new PlanState(), read.State);
        Assert.Equal(evidence, await f.Store.ReadEvidenceAsync(f.Definition.PlanId, hash, Token));
        Assert.Equal(PlanStoreFixture.Changes, File.ReadAllText(Path.Combine(f.Root, f.Definition.PlanId, "changes.txt")));
        Assert.Equal(PlanStoreFixture.Changes, await f.Store.ReadChangesAsync(f.Definition.PlanId, Token));
        string audit = File.ReadAllText(Assert.Single(Directory.GetFiles(f.Locations.Audit, "*.jsonl")));
        Assert.Contains(published.PlanHash, audit, StringComparison.Ordinal);
        await Assert.ThrowsAsync<IOException>(() => f.Publish());
    }

    [Fact]
    public async Task ContentAddressedEvidence_IsIdempotentAndRefusesTampering()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        byte[] bytes = Encoding.UTF8.GetBytes("{\"after\":1}");
        string hash = await f.Store.PutEvidenceAsync(f.Definition.PlanId, bytes, Token);
        Assert.Equal(hash, await f.Store.PutEvidenceAsync(f.Definition.PlanId, bytes, Token));
        Assert.Equal(bytes, await f.Store.ReadEvidenceAsync(f.Definition.PlanId, hash, Token));
        File.WriteAllText(Path.Combine(f.Root, f.Definition.PlanId, "evidence", hash + ".json"), "{}");
        await Assert.ThrowsAsync<JsonException>(() => f.Store.ReadEvidenceAsync(f.Definition.PlanId, hash, Token));
        File.WriteAllText(Path.Combine(f.Root, f.Definition.PlanId, "changes.txt"), "changed");
        await Assert.ThrowsAsync<JsonException>(() => f.Store.ReadChangesAsync(f.Definition.PlanId, Token));
    }

    [Fact]
    public async Task Evidence_CrashBeforeRename_NeverPublishesPartialContent()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        byte[] bytes = Encoding.UTF8.GetBytes("{}");
        var store = new PlanStore(f.Locations, f.Audit) { Checkpoint = p => { if (p == "evidence_staged") { throw new IOException("Simulated crash."); } } };
        await Assert.ThrowsAsync<IOException>(() => store.PutEvidenceAsync(f.Definition.PlanId, bytes, Token));
        await Assert.ThrowsAsync<FileNotFoundException>(() => f.Store.ReadEvidenceAsync(f.Definition.PlanId, PlanStore.Hash(bytes), Token));
        Assert.NotNull(await f.Store.ReadAsync(f.Definition.PlanId, Token));
    }

    [Theory]
    [InlineData("{\"fingerprint\":\"session\"}")]
    [InlineData("{\"nested\":{\"DatabaseIdentifier\":\"runtime\"}}")]
    [InlineData("{\"x\":1,\"x\":2}")]
    public async Task Evidence_RuntimeIdentifiersAndDuplicateMembers_AreNeverWritten(string json)
    {
        using var f = new PlanStoreFixture();
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await Assert.ThrowsAsync<JsonException>(() => f.Store.PublishAsync(f.Definition, PlanStoreFixture.Changes, new Dictionary<string, byte[]> { [PlanStore.Hash(bytes)] = bytes }, Token));
        Assert.False(File.Exists(f.Record));
        await f.Publish();
        await Assert.ThrowsAsync<JsonException>(() => f.Store.PutEvidenceAsync(f.Definition.PlanId, bytes, Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, f.Definition.PlanId, "evidence")));
    }

    [Theory]
    [InlineData("flushed", false)]
    [InlineData("staged", false)]
    [InlineData("audited", false)]
    [InlineData("evidence_published", false)]
    [InlineData("published", true)]
    public async Task Publish_CrashAtEachBoundary_OnlyFinalMoveMakesAPlan(string point, bool visible)
    {
        using var f = new PlanStoreFixture();
        var store = new PlanStore(f.Locations, f.Audit) { Checkpoint = p => { if (p == point) { throw new IOException("Simulated crash."); } } };
        await Assert.ThrowsAsync<IOException>(() => f.Publish(store));
        Assert.Equal(visible, await f.Store.ReadAsync(f.Definition.PlanId, Token) is not null);
        if (visible)
        {
            Assert.True(File.Exists(Path.Combine(f.Root, f.Definition.PlanId, "changes.txt")));
        }
    }

    [Fact]
    public async Task Publish_AuditFailure_NeverPublishes()
    {
        using var f = new PlanStoreFixture();
        Directory.CreateDirectory(f.Locations.Root);
        File.WriteAllText(f.Locations.Audit, "blocked");
        await Assert.ThrowsAsync<AuditUnavailableException>(() => f.Publish());
        Assert.Null(await f.Store.ReadAsync(f.Definition.PlanId, Token));
    }

    [Theory]
    [InlineData("replacement_staged", 0)]
    [InlineData("replaced", 1)]
    public async Task Update_Crash_LeavesWholeOldOrNewRecord(string point, long revision)
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        var store = new PlanStore(f.Locations, f.Audit) { Checkpoint = p => { if (p == point) { throw new IOException("Simulated crash."); } } };
        await Assert.ThrowsAsync<IOException>(() => store.UpdateAsync(f.Definition.PlanId, 0, s => s.Admit("attempt", "task", "host"), Token));
        Assert.Equal(revision, (await f.Store.ReadAsync(f.Definition.PlanId, Token))!.State.Revision);
    }

    [Fact]
    public async Task Update_ConcurrentWriters_OneWinsCasAndReadersSeeWholeSnapshots()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        Task<PlanRecord?>[] writers;
        using (var old = new FileStream(f.Record, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() => f.Store.UpdateAsync(f.Definition.PlanId, 0, s => s.Admit("attempt", "task", "host"), Token), Token)).ToArray();
            using var oldReader = new StreamReader(old);
            Assert.Equal(0, PlanJson.Read<PlanRecord>(await oldReader.ReadToEndAsync(Token)).State.Revision);
        }
        Task[] readers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                PlanRecord record = Assert.IsType<PlanRecord>(await f.Store.ReadAsync(f.Definition.PlanId, Token));
                Assert.InRange(record.State.Revision, 0, 1);
                Assert.Equal(record.State.Revision == 0, record.State.AttemptId is null);
            }
        }, Token)).ToArray();
        PlanRecord?[] results = await Task.WhenAll(writers);
        await Task.WhenAll(readers);
        Assert.Single(results.OfType<PlanRecord>());
        Assert.Null(await f.Store.UpdateAsync(f.Definition.PlanId, 0, _ => throw new InvalidOperationException("Must not run."), Token));
        await f.Store.UpdateAsync(f.Definition.PlanId, 1, s => s.Start(PlanTestData.Created, PolicyVerdict.Allow), Token);
        Assert.Equal(2, (await f.Store.ReadAsync(f.Definition.PlanId, Token))!.State.Revision);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"extra\":true}")]
    public async Task Read_UnknownOrMalformed_IsPreservedAndRefused(string json)
    {
        using var f = new PlanStoreFixture();
        Directory.CreateDirectory(f.Root);
        File.WriteAllText(f.Record, json);
        await Assert.ThrowsAnyAsync<Exception>(() => f.Store.ReadAsync(f.Definition.PlanId, Token));
        Assert.Equal(json, File.ReadAllText(f.Record));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("plans")]
    [InlineData("assets")]
    [InlineData("evidence")]
    public async Task Links_AtEveryDirectoryLevel_AreRefused(string level)
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        string path = level switch
        {
            "state" => f.Locations.State,
            "plans" => f.Root,
            "assets" => Path.Combine(f.Root, f.Definition.PlanId),
            _ => Path.Combine(f.Root, f.Definition.PlanId, "evidence"),
        };
        string target = Path.Combine(f.Locations.Root, "target");
        Directory.Move(path, target);
        await PlanStoreFixture.Junction(path, target);
        try
        { await Assert.ThrowsAsync<IOException>(() => f.Store.ReadAsync(f.Definition.PlanId, Token)); }
        finally { Directory.Delete(path); }
    }
}
