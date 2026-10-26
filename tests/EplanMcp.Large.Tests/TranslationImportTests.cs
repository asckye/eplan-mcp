using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Writes the configured translation database only after the owner approves this run and opts in (README.md).
/// Copies and hashes the entire translation folder before any import. The recovery copy outlives the Host;
/// the operator closes EPLAN, restores that copy and verifies its manifest afterwards. Never restores automatically.
/// </summary>
[Trait("Size", "Large")]
public sealed class TranslationImportTests
{
    public const string OptIn = PartImportTests.OptIn;
    public const string TestEntry = "EplanMcpLargeTest";
    private const string Tool = "eplan_translation_import";
    private const string FileName = "eplanmcp-large-translations.txt";

    [Fact]
    public async Task TestEntry_IsAddedOnceAndReimportAddsNothing_WithApprovalAndBackupEveryTime()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(OptIn) == "1",
            $"Writes the configured translation database: the owner must approve each run before setting {OptIn}=1 (README.md).");
        await using var host = new DestructiveHost { RequireWritable = false };
        await host.InitializeAsync();
        string imports = new HostDataLocations(host.Home).Imports;
        Directory.CreateDirectory(imports);
        string source = Path.Combine(imports, FileName);
        File.WriteAllText(source, "en_US\tzh_CN\r\n" + TestEntry + "\tEplanMcpLargeTest 译文\r\n", Encoding.Unicode);
        await using var bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        WritePlan plan = await bridge.InvokeAsync<TranslationImportArgs, WritePlan>(OperationNames.TranslationImportPlan,
            new TranslationImportArgs { File = FileName, FilePath = source, Sha256 = Hash(source) }, TestContext.Current.CancellationToken);
        Assert.True(Path.IsPathFullyQualified(plan.MasterData!.Path), "The configured translation database must resolve to a local file.");
        Assert.True(File.Exists(plan.MasterData.Path));
        string recovery = CopyTranslationFolder(plan.MasterData.Path);
        TestContext.Current.TestOutputHelper?.WriteLine("Recovery copy and SHA-256 manifest retained at: " + recovery);

        JsonElement exported = await host.CallAsync("eplan_translation_export", new() { ["format"] = "tab" });
        string exportFile = Path.Combine(exported.GetProperty("directory").GetString()!, exported.GetProperty("files")[0].GetProperty("path").GetString()!);
        TranslationTable before = TranslationTable.Parse(File.ReadAllBytes(exportFile), export: true);
        int reference = Array.IndexOf(before.Columns, "en_US");
        Assert.True(reference >= 0);
        Assert.DoesNotContain(before.Rows, row => row[reference] == TestEntry);
        Dictionary<string, object?> args = new() { ["file"] = FileName };
        JsonElement dryRun = await host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.Equal(1, dryRun.GetProperty("entries").GetInt32());
        Assert.Equal(JsonValueKind.Null, dryRun.GetProperty("added").ValueKind);
        Assert.Empty(host.Approvals.Questions);

        host.Approvals.Approve(Tool, string.Empty, WritePipeline.ChangeLines(plan));
        JsonElement imported = await host.CallAsync(Tool, args);
        Verify(imported, added: 1);
        Assert.Single(host.Approvals.Questions);
        host.Approvals.Approve(Tool, string.Empty, WritePipeline.ChangeLines(plan));
        JsonElement again = await host.CallAsync(Tool, args);
        Verify(again, added: 0);
        Assert.Equal(2, host.Approvals.Questions.Count);
        Assert.NotEqual(imported.GetProperty("backup").GetString(), again.GetProperty("backup").GetString());
        Assert.NotEqual(imported.GetProperty("auditId").GetString(), again.GetProperty("auditId").GetString());
        Assert.True(Directory.Exists(recovery), "Keep the recovery copy until the operator has restored and checked it.");
    }

    private static void Verify(JsonElement result, int added)
    {
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Equal((added, 0, 0), (result.GetProperty("added").GetInt32(), result.GetProperty("filled").GetInt32(), result.GetProperty("kept").GetInt32()));
        Assert.False(string.IsNullOrEmpty(result.GetProperty("auditId").GetString()));
        string backup = result.GetProperty("backup").GetString()!;
        Assert.EndsWith(".zw7", backup, StringComparison.OrdinalIgnoreCase);
        Assert.True(new FileInfo(backup).Length > 0);
    }

    private static string CopyTranslationFolder(string database)
    {
        string folder = Path.GetDirectoryName(database)!;
        Dictionary<string, string> hashes = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), Hash, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(hashes);
        string recovery = Directory.CreateTempSubdirectory("eplanmcp-translation-backup-").FullName;
        File.WriteAllText(Path.Combine(recovery, "manifest.json"), JsonSerializer.Serialize(new { database, folder, files = hashes }));
        foreach ((string relative, string expected) in hashes)
        {
            string copy = Path.Combine(recovery, "files", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Path.Combine(folder, relative), copy);
            Assert.Equal(expected, Hash(copy));
            Assert.Equal(expected, Hash(Path.Combine(folder, relative)));
        }

        Assert.Equal(hashes.Keys.Order(StringComparer.OrdinalIgnoreCase), Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file)).Order(StringComparer.OrdinalIgnoreCase));
        foreach ((string relative, string expected) in hashes)
        {
            Assert.Equal(expected, Hash(Path.Combine(folder, relative)));
        }

        return recovery;
    }

    private static string Hash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
