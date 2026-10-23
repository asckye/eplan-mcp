using System.Globalization;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanStoreMaintenanceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cleanup_ReadsNeverExtendRetention_AndDeletesOnlyExpiredOrdinaryPlans()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        var clock = new PlanTestClock(f.Definition.ExpiresAt.AddDays(30).AddTicks(-1));
        var store = new PlanStore(f.Locations, f.Audit) { Clock = clock };
        await store.CleanupAsync(Token);
        byte[] before = File.ReadAllBytes(f.Record);
        Assert.NotNull(await store.ReadAsync(f.Definition.PlanId, Token));
        Assert.Equal(before, File.ReadAllBytes(f.Record));
        clock.Now = clock.Now.AddTicks(1);
        await store.CleanupAsync(Token);
        Assert.Null(await store.ReadAsync(f.Definition.PlanId, Token));
        Assert.False(Directory.Exists(Path.Combine(f.Root, f.Definition.PlanId)));
        Assert.NotEmpty(Directory.GetFiles(f.Locations.Audit, "*.jsonl"));
    }

    [Fact]
    public async Task Cleanup_ActiveAndUnverifiedUnknownSurviveIndefinitely()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        await f.Store.UpdateAsync(f.Definition.PlanId, 0, s => s.Admit("a", "t", "h"), Token);
        var store = new PlanStore(f.Locations, f.Audit) { Clock = new PlanTestClock(f.Definition.ExpiresAt.AddYears(20)) };
        await store.CleanupAsync(Token);
        Assert.NotNull(await store.ReadAsync(f.Definition.PlanId, Token));
        await store.UpdateAsync(f.Definition.PlanId, 1, s => s.Start(f.Definition.CreatedAt, PolicyVerdict.Allow), Token);
        PlanResult unknown = PlanResult.Aggregate([new(0, "page.update", true, "unknown", new("OUTCOME_UNKNOWN", "Check the write.", false))]);
        await store.UpdateAsync(f.Definition.PlanId, 2, s => s.Complete(unknown, f.Definition.CreatedAt), Token);
        await store.CleanupAsync(Token);
        Assert.Equal(PlanStatus.Unknown, (await store.ReadAsync(f.Definition.PlanId, Token))!.State.Status);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":99,\"unverifiedWrites\":true}")]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    public async Task Cleanup_QuarantinesUnknownAndCorruptBytes_Permanently(string json)
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        File.WriteAllText(f.Record, json);
        byte[] bytes = File.ReadAllBytes(f.Record);
        var store = new PlanStore(f.Locations, f.Audit) { Clock = new PlanTestClock(f.Definition.ExpiresAt.AddYears(20)) };
        await store.CleanupAsync(Token);
        Assert.Null(await store.ReadAsync(f.Definition.PlanId, Token));
        string record = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "quarantine"), "record.json", SearchOption.AllDirectories));
        Assert.Equal(bytes, File.ReadAllBytes(record));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(record)!, f.Definition.PlanId, "changes.txt")));
        await store.CleanupAsync(Token);
        (int count, long size, int quarantined) = await store.UsageAsync(Token);
        Assert.Equal(1, count);
        Assert.Equal(1, quarantined);
        Assert.True(size > bytes.Length);
        Assert.Equal(DoctorStatus.Warning, DoctorChecks.Plans(f.Locations).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Publish());
    }

    [Fact]
    public async Task Admission_ThirtyTwoFullReservations_BlockTheNextPlanWithoutEvicting()
    {
        using var f = new PlanStoreFixture();
        PlanStore store = f.Store;
        for (int i = 0; i < 31; i++)
        {
            PlanDefinition definition = f.Definition with { PlanId = i.ToString("x32", CultureInfo.InvariantCulture) };
            await store.PublishAsync(definition, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        }
        async Task<bool> Admit(string id)
        {
            try
            {
                await f.Store.PublishAsync(f.Definition with { PlanId = id }, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
                return true;
            }
            catch (PlanCapacityException) { return false; }
        }
        bool[] admitted = await Task.WhenAll(Admit(new string('a', 32)), Admit(new string('b', 32)));
        Assert.Single(admitted, accepted => accepted);
        PlanCapacityException error = await Assert.ThrowsAsync<PlanCapacityException>(() => f.Publish());
        Assert.Equal(PlanCapacity.Total, error.Metadata["used"]!.GetValue<long>());
        Assert.Equal(32, (await store.UsageAsync(Token)).Count);
        Assert.Equal(32, Directory.GetFiles(f.Root, "*.json").Length);
    }

    [Fact]
    public async Task Admission_ExpiredRetainedPlans_KeepNoReservation()
    {
        using var f = new PlanStoreFixture();
        for (int i = 0; i < 32; i++)
        {
            await f.Store.PublishAsync(f.Definition with { PlanId = i.ToString("x32", CultureInfo.InvariantCulture) }, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        }

        // A day later the 32 compiled plans can no longer run: they stay retained, but count their actual bytes only.
        var later = new PlanStore(f.Locations, f.Audit) { Clock = new PlanTestClock(f.Definition.ExpiresAt.AddHours(1)) };
        PlanDefinition next = f.Definition with
        {
            PlanId = new string('c', 32),
            CreatedAt = f.Definition.ExpiresAt.AddHours(1),
            ExpiresAt = f.Definition.ExpiresAt.AddHours(25),
        };
        await later.PublishAsync(next, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        Assert.Equal(33, (await later.UsageAsync(Token)).Count);
    }

    [Fact]
    public async Task Quarantine_UsesTheRecordQuotaToo()
    {
        using var f = new PlanStoreFixture();
        for (int i = 0; i < PlanCapacity.Records; i++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(f.Root, "quarantine", i.ToString(CultureInfo.InvariantCulture))).FullName;
            File.WriteAllText(Path.Combine(folder, "record.json"), "unknown");
        }
        PlanCapacityException error = await Assert.ThrowsAsync<PlanCapacityException>(() => f.Publish());
        Assert.Equal("plans.records", error.Metadata["resource"]!.GetValue<string>());
        Assert.Equal(1000, (await f.Store.UsageAsync(Token)).Count);
    }

    [Fact]
    public async Task EvidenceQuota_StopsCollectionAndKeepsTheKnownState()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        string evidence = "\"" + new string('x', (int)(13 * PlanCapacity.MiB)) + "\"";
        await Assert.ThrowsAsync<PlanCapacityException>(() => f.Store.PutEvidenceAsync(f.Definition.PlanId, System.Text.Encoding.UTF8.GetBytes(evidence), Token));
        Assert.Equal(0, (await f.Store.ReadAsync(f.Definition.PlanId, Token))!.State.Revision);
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, f.Definition.PlanId, "evidence")));
    }

    [Fact]
    public async Task Cleanup_RefusesLinksBeforeDeletingExpiredRecords()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        string target = Directory.CreateDirectory(Path.Combine(f.Locations.Root, "outside")).FullName;
        string marker = Path.Combine(target, "keep.txt");
        File.WriteAllText(marker, "keep");
        string link = Path.Combine(f.Root, "quarantine");
        await PlanStoreFixture.Junction(link, target);
        try
        {
            var store = new PlanStore(f.Locations, f.Audit) { Clock = new PlanTestClock(f.Definition.ExpiresAt.AddYears(1)) };
            await Assert.ThrowsAsync<IOException>(() => store.CleanupAsync(Token));
            Assert.True(File.Exists(f.Record));
            Assert.Equal("keep", File.ReadAllText(marker));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task Admission_QuarantineAndCrashLeftoversCountAgainstCapacity()
    {
        using var f = new PlanStoreFixture();
        Directory.CreateDirectory(f.Root);
        File.WriteAllText(f.Record, "corrupt");
        using (var stream = new FileStream(Path.Combine(f.Root, ".partial-residue"), FileMode.CreateNew))
        {
            stream.SetLength(PlanCapacity.Total - PlanCapacity.PerPlan + 1);
        }
        await Assert.ThrowsAsync<PlanCapacityException>(() => f.Store.PublishAsync(f.Definition with { PlanId = new string('a', 32) }, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token));
        Assert.Equal(1, (await f.Store.UsageAsync(Token)).Quarantined);
        Assert.Empty(Directory.GetFiles(f.Root, "*.json"));
    }

    [Fact]
    public async Task Admission_CleansExpiredRecords_AndHonorsConfiguredLifetime()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        var clock = new PlanTestClock(f.Definition.ExpiresAt.AddDays(30));
        var store = new PlanStore(f.Locations, f.Audit) { Clock = clock, LifetimeHours = 1 };
        PlanDefinition next = f.Definition with { PlanId = new string('a', 32), CreatedAt = clock.Now, ExpiresAt = clock.Now.AddHours(1) };
        await store.PublishAsync(next, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token);
        Assert.Null(await store.ReadAsync(f.Definition.PlanId, Token));
        Assert.Equal(1, (await store.UsageAsync(Token)).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => store.PublishAsync(next with { PlanId = new string('b', 32), ExpiresAt = clock.Now.AddHours(2) }, PlanStoreFixture.Changes, new Dictionary<string, byte[]>(), Token));
        File.WriteAllText(f.Locations.ConfigFile, "{\"planLifetimeHours\":1}");
        Assert.Contains("planLifetimeHours: 1 h", DoctorChecks.Configuration(f.Locations).English, StringComparison.Ordinal);
        Assert.Contains("1 records", DoctorChecks.Plans(f.Locations).English, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateQuota_RejectsOversizeResultWithoutReplacingTheRecord()
    {
        using var f = new PlanStoreFixture();
        await f.Publish();
        await f.Store.UpdateAsync(f.Definition.PlanId, 0, s => s.Admit("a", "t", "h"), Token);
        PlanResult result = PlanResult.Aggregate([], new("INTERNAL", new string('x', (int)PlanCapacity.MiB), false));
        await Assert.ThrowsAsync<PlanCapacityException>(() => f.Store.UpdateAsync(f.Definition.PlanId, 1, s => s.Complete(result, f.Definition.CreatedAt), Token));
        Assert.Equal(1, (await f.Store.ReadAsync(f.Definition.PlanId, Token))!.State.Revision);
    }
}
