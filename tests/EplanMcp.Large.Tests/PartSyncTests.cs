using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_part_sync on the write copy through <see cref="DestructiveHost"/> (docs/tools/eplan_part_sync.md, section 7).
/// It WRITES THE CONFIGURED PARTS DATABASE, so it is skipped unless <see cref="PartImportTests.OptIn"/> is 1 (README.md:
/// the owner approves each run; the maintainer hashes and copies the database first and restores it afterwards), and it
/// changes the copy for good: EPLAN stores the parts the project referenced in it (S24 r15: 293 to 341). The first run on
/// a fresh copy synchronizes; later runs find nothing to do. What the spec leaves open goes to the test output.
/// </summary>
[Trait("Size", "Large")]
public sealed class PartSyncTests
{
    private const string Tool = "eplan_part_sync";

    [Fact]
    public async Task TheCopysParts_AreSynchronizedAfterTwoBackups_ThenThereIsNothingToDo()
    {
        PartImportTests.SkipUnlessOptedIn();
        await using var host = new DestructiveHost();
        await host.InitializeAsync();
        Dictionary<string, object?> args = new() { ["project"] = host.Writable };

        JsonElement dryRun = await host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        int added = dryRun.GetProperty("addedCount").GetInt32();
        int stored = dryRun.GetProperty("storedInProjectCount").GetInt32();
        int before = dryRun.GetProperty("partsBefore").GetInt32();
        int projectBefore = dryRun.GetProperty("projectPartsBefore").GetInt32();
        string project = dryRun.GetProperty("project").GetString()!;
        TestContext.Current.TestOutputHelper?.WriteLine($"dry run: {added} to add, {stored} to store in {project} (now {projectBefore}), {before} parts in {dryRun.GetProperty("database").GetString()}");
        Assert.Equal("not_started", dryRun.GetProperty("execution").GetString());
        Assert.Empty(host.Approvals.Questions);
        if (added + stored > 0)
        {
            await SynchronizeAsync(host, args, project, added, stored, before, projectBefore);
        }
        else
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Nothing to synchronize: an earlier run stored the referenced parts; copy the project again to repeat the first run (README.md).");
        }

        // Now there is nothing to do: nobody is asked and nothing is backed up.
        int asked = host.Approvals.Questions.Count;
        JsonElement again = await host.CallAsync(Tool, args);

        Assert.Equal(("not_started", 0, 0), (again.GetProperty("execution").GetString(), again.GetProperty("addedCount").GetInt32(), again.GetProperty("storedInProjectCount").GetInt32()));
        Assert.Equal(asked, host.Approvals.Questions.Count);
    }

    private static async Task SynchronizeAsync(DestructiveHost host, Dictionary<string, object?> args, string project, int added, int stored, int before, int projectBefore)
    {
        var lines = new List<string>();
        if (added > 0)
        {
            lines.Add($"add {Parts(added)} from {project} to the parts database");
        }

        if (stored > 0)
        {
            lines.Add($"store {Parts(stored)} the project references in project {project} (now {projectBefore})");
        }

        host.Approvals.Approve(Tool, host.Writable, lines);
        var clock = Stopwatch.StartNew();
        JsonElement synchronized = await host.CallAsync(Tool, args);
        clock.Stop();

        Assert.Equal(("committed", "AI: eplan_part_sync"), (synchronized.GetProperty("execution").GetString(), synchronized.GetProperty("undo").GetString()));
        Assert.Empty(synchronized.GetProperty("missing").EnumerateArray());
        Assert.Empty(synchronized.GetProperty("notStored").EnumerateArray());
        int after = synchronized.GetProperty("partsAfter").GetInt32();
        int projectAfter = synchronized.GetProperty("projectPartsAfter").GetInt32();
        Assert.True(after >= before + added, $"{after} parts in the database after adding {added} to {before}");
        Assert.True(projectAfter >= projectBefore + stored, $"{projectAfter} parts in the project after storing {stored} in {projectBefore}");
        string backup = synchronized.GetProperty("backup").GetString()!;
        string projectBackup = synchronized.GetProperty("projectBackup").GetString()!;
        Assert.True(backup.EndsWith(".zw6", StringComparison.OrdinalIgnoreCase) && new FileInfo(backup).Length > 0, "No parts database backup at " + backup);
        Assert.True(File.Exists(projectBackup), "No project backup at " + projectBackup);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"synchronized in {clock.Elapsed.TotalSeconds:0.0} s (both backups included): database {before} -> {after}, project {projectBefore} -> {projectAfter}; hint: {synchronized.GetProperty("hint").GetString()}");
        TestContext.Current.TestOutputHelper?.WriteLine("Check EPLAN's system messages for S328014 (write access without lock, S24 r15) and record the result in api-notes A30.");
    }

    private static string Parts(int count) => count == 1 ? "1 part" : count + " parts";
}
