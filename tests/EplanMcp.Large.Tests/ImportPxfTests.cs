using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_import_pxf through the write tests' eplanmcp, whose data root is a temporary folder and whose only scratch
/// root is <c>&lt;scratch root&gt;\writes</c> (README.md; docs/tools/eplan_import_pxf.md, section 7). The test exports the
/// write copy as an EPJ file with eplan_export_pxf, moves it into that data root's <c>imports</c> folder, and imports it
/// as a new project in <c>writes\imported</c>, which EPLAN does not open. The newest imported project is kept, so the
/// engineer can open it and compare its pages with the copy's; earlier ones are deleted at the start of a run.
/// </summary>
[Trait("Size", "Large")]
public sealed class ImportPxfTests(WriteHost host) : IClassFixture<WriteHost>
{
    private const string NamePrefix = "LargeImport_";

    private static string Imported => Path.Combine(WriteHost.ScratchFolder, "imported");

    [Fact]
    public async Task TheWriteCopysEpj_BecomesANewProjectInScratch_AndTheNameOnlyOnce()
    {
        DeleteEarlierImports();
        string file = await ExportCopyAsync();
        string name = NamePrefix + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        string target = Path.Combine(Imported, name);

        JsonElement dryRun = await host.CallAsync("eplan_import_pxf", new() { ["file"] = file, ["name"] = name, ["dry_run"] = true });
        Assert.Equal(("not_started", target + ".elk"), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("project").GetProperty("path").GetString()));
        Assert.False(File.Exists(target + ".elk"));

        var clock = Stopwatch.StartNew();
        JsonElement imported = await host.CallAsync("eplan_import_pxf", new() { ["file"] = file, ["name"] = name });
        double seconds = clock.Elapsed.TotalSeconds;
        JsonElement again = await host.ErrorAsync("eplan_import_pxf", new() { ["file"] = file, ["name"] = name });

        // S22 (A29): the target is the project's path without extension; EPLAN makes the .elk and the .edb, and does not
        // open the project.
        JsonElement project = imported.GetProperty("project");
        Assert.Equal(("committed", name, target + ".elk", false), (imported.GetProperty("execution").GetString(), project.GetProperty("name").GetString(), project.GetProperty("path").GetString(), project.GetProperty("opened").GetBoolean()));
        Assert.True(File.Exists(target + ".elk"));
        Assert.True(Directory.Exists(target + ".edb"));
        string[] open = [.. (await host.CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        Assert.DoesNotContain(open, p => p.StartsWith(target, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString()));

        // Writes in scratch are never approved, and the audit names the file and the new project (tool spec 5).
        Assert.DoesNotContain(host.AuditRecords(), r => (string?)r["kind"] == "approval" && (string?)r["tool"] == "eplan_import_pxf");
        Assert.Contains(host.AuditRecords(), r => (string?)r["kind"] == "execution_started" && (string?)r["tool"] == "eplan_import_pxf" && (string?)r["project"] == target + ".elk");
        // 'seconds' is null when the response came after the Host stopped waiting and was recovered (recovered: true).
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{imported.GetProperty("bytes").GetInt64() / (1024 * 1024)} MB imported in {imported.GetProperty("seconds")} s ({seconds:0} s for the call, recovered: {imported.GetProperty("recovered").GetBoolean()}) as {target}.elk. Open it in EPLAN and compare its pages with the write copy's.");
    }

    [Fact]
    public async Task AMissingFile_AndANameWithADot_ImportNothing()
    {
        JsonElement missing = await host.ErrorAsync("eplan_import_pxf", new() { ["file"] = "large/missing.epj", ["name"] = NamePrefix + "missing" });
        JsonElement dotted = await host.ErrorAsync("eplan_import_pxf", new() { ["file"] = "large/missing.epj", ["name"] = NamePrefix + "v1.2" });

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted), (missing.GetProperty("reason").GetString(), missing.GetProperty("execution").GetString()));
        Assert.Equal(ErrorReason.InvalidArgument, dotted.GetProperty("reason").GetString());
        Assert.False(File.Exists(Path.Combine(Imported, NamePrefix + "missing.elk")));
    }

    /// <summary>The write copy as an EPJ file without master data (S25: about 600 MB), moved into this eplanmcp's imports folder.</summary>
    private async Task<string> ExportCopyAsync()
    {
        JsonElement export = await host.CallAsync("eplan_export_pxf", new() { ["project"] = host.Writable, ["file_name"] = "LargeImport" });
        string epj = Path.Combine(export.GetProperty("directory").GetString()!, "LargeImport.epj");
        string imports = Directory.CreateDirectory(Path.Combine(host.Home, "imports", "large")).FullName;
        File.Move(epj, Path.Combine(imports, "LargeImport.epj"), overwrite: true);
        return "large/LargeImport.epj";
    }

    /// <summary>Earlier runs' projects; one the engineer has open in EPLAN cannot be deleted and stays.</summary>
    private static void DeleteEarlierImports()
    {
        if (!Directory.Exists(Imported))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(Imported, NamePrefix + "*"))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (IOException)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"Could not delete {entry}; close it in EPLAN.");
            }
        }
    }
}
