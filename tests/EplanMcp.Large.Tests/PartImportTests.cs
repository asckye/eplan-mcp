using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Host;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_part_import on the parts database EPLAN is configured with, through <see cref="DestructiveHost"/>
/// (docs/tools/eplan_part_import.md, section 7). It WRITES THAT DATABASE: S24 found that no copy isolates an import
/// (conclusion 8), so it is skipped unless <see cref="OptIn"/> is 1, which the maintainer sets only for a run the owner
/// approved, after hashing and copying the database's files; afterwards the maintainer closes EPLAN and restores them
/// (README.md). It imports one test part, <see cref="TestPart"/>, from a file it writes into its own import folder: added
/// once after a backup, then left as it is. What the spec leaves open goes to the test output.
/// </summary>
[Trait("Size", "Large")]
public sealed class PartImportTests
{
    /// <summary>The environment variable that lets the tests that write the configured parts database run.</summary>
    public const string OptIn = "EPLANMCP_LARGE_MASTERDATA";

    /// <summary>The part the tests add; the database must not have it before a run.</summary>
    public const string TestPart = "EPLANMCP.LARGE-TEST";

    private const string Tool = "eplan_part_import";
    private const string FileName = "eplanmcp-large-test.xml";

    // The structure S24 exported (api-notes A30) with the fields its test part set; whether EPLAN takes a file this
    // short is part of what the test finds out.
    private const string TestFile = """
        <?xml version="1.0" encoding="utf-8" ?>
        <partsmanagement count="1" type="EPLAN.PartsManagement">
         <part P_ARTICLE_PARTNR="EPLANMCP.LARGE-TEST" P_ARTICLE_DESCR1="??_??@EplanMcp Large test part;" P_ARTICLE_PARTTYPE="1">
          <variant P_ARTICLE_VARIANT="1" />
         </part>
        </partsmanagement>
        """;

    /// <summary>Skips unless the run was opted into (README.md), before anything connects to EPLAN.</summary>
    internal static void SkipUnlessOptedIn() => Assert.SkipUnless(
        Environment.GetEnvironmentVariable(OptIn) == "1",
        $"Writes the parts database EPLAN is configured with: run only after the owner approved the run and the database's files were hashed and copied, with {OptIn}=1 (README.md).");

    [Fact]
    public async Task ATestPart_IsAddedOnceAfterABackup_ThenLeftAsItIs()
    {
        SkipUnlessOptedIn();
        await using var host = new DestructiveHost { RequireWritable = false };
        await host.InitializeAsync();
        string imports = new HostDataLocations(host.Home).Imports;
        Directory.CreateDirectory(imports);
        File.WriteAllText(Path.Combine(imports, FileName), TestFile);
        Dictionary<string, object?> args = new() { ["file"] = FileName };

        JsonElement dryRun = await host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.True(Strings(dryRun, "added").SequenceEqual([TestPart]), $"The parts database has {TestPart} already: restore it from the copy made before the last run (README.md).");
        int before = dryRun.GetProperty("partsBefore").GetInt32();
        Assert.Empty(host.Approvals.Questions);

        host.Approvals.Approve(Tool, string.Empty, [$"add 1 part from {FileName} to the parts database"]);
        var clock = Stopwatch.StartNew();
        JsonElement imported = await host.CallAsync(Tool, args);
        clock.Stop();

        Assert.Equal("committed", imported.GetProperty("execution").GetString());
        Assert.Equal([TestPart], Strings(imported, "added"));
        Assert.Equal(before + 1, imported.GetProperty("partsAfter").GetInt32());
        Assert.Empty(Strings(imported, "missing"));
        string backup = imported.GetProperty("backup").GetString()!;
        Assert.StartsWith(Path.Combine(host.Home, "backups", "masterdata") + Path.DirectorySeparatorChar, backup, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".zw6", backup, StringComparison.OrdinalIgnoreCase);
        long archive = new FileInfo(backup).Length;
        Assert.True(archive > 0, "The backup archive is empty: " + backup);
        int asked = host.Approvals.Questions.Count;

        // Imported again, the part is there: nothing to do, so nobody is asked and nothing is backed up.
        JsonElement again = await host.CallAsync(Tool, args);

        Assert.Equal("not_started", again.GetProperty("execution").GetString());
        Assert.Equal([TestPart], Strings(again, "skipped"));
        Assert.Equal(asked, host.Approvals.Questions.Count);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"database {imported.GetProperty("database").GetString()}: {before} -> {imported.GetProperty("partsAfter").GetInt32()} parts in {clock.Elapsed.TotalSeconds:0.0} s (backup and import); backup {archive} bytes; hint: {imported.GetProperty("hint").GetString()}");
        TestContext.Current.TestOutputHelper?.WriteLine("approval dialog changes: " + string.Join(" | ", host.Approvals.Questions[0].Changes));
    }

    private static List<string> Strings(JsonElement result, string name) => [.. result.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];
}
