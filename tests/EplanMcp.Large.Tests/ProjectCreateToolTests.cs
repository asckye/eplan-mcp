using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>Tool spec 7: fixed imports, both modes, source integrity, compatibility, closed projects and no reuse.</summary>
[Trait("Size", "Large")]
public sealed class ProjectCreateToolTests(WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData("basic_project")]
    [InlineData("backup")]
    public async Task CreatesACompatibleClosedProject_WithoutChangingTheArchive_AndNeverReusesTheDestination(string mode)
    {
        string? source = mode == "backup" ? Environment.GetEnvironmentVariable(ProjectRestoreTests.ArchiveVariable)
            : Environment.GetEnvironmentVariable(ProjectCreateTests.BasicProjectVariable) is { Length: > 0 } given ? given : ProjectCreateTests.Find("GB_bas001.zw9");
        Assert.SkipWhen(source is null || !File.Exists(source), mode == "backup"
            ? $"Set {ProjectRestoreTests.ArchiveVariable} to a non-customer sample backup with database version 18.0."
            : $"No unique GB_bas001.zw9 found; set {ProjectCreateTests.BasicProjectVariable} (one-page basic project).");
        string originalHash = Hash(source!);
        string relative = "large/create-tool/" + Path.GetFileName(source!);
        string imported = Path.Combine(host.Home, "imports", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        File.Copy(source!, imported, overwrite: true);
        string name = "LargeCreate_" + mode + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        string destination = Path.Combine(WriteHost.ScratchFolder, "projects", name);
        string link = Path.Combine(destination, name + ".elk");
        Dictionary<string, object?> Args(bool dryRun = false) => new() { ["mode"] = mode, ["file"] = relative, ["name"] = name, ["dry_run"] = dryRun };

        JsonElement dry = await host.CallAsync("eplan_project_create", Args(dryRun: true));
        Assert.Equal(("not_started", link, originalHash), (dry.GetProperty("execution").GetString(), dry.GetProperty("project").GetProperty("path").GetString(), dry.GetProperty("sha256").GetString()));
        Assert.False(Directory.Exists(destination));
        Assert.Equal(("create", "project", name), (dry.GetProperty("changes")[0].GetProperty("action").GetString(), dry.GetProperty("changes")[0].GetProperty("kind").GetString(), dry.GetProperty("changes")[0].GetProperty("name").GetString()));
        JsonElement result = await host.CallAsync("eplan_project_create", Args());
        Assert.Equal(("committed", "18.0", originalHash), (result.GetProperty("execution").GetString(), result.GetProperty("databaseVersion").GetString(), result.GetProperty("sha256").GetString()));
        Assert.Equal((name, link, false), (result.GetProperty("project").GetProperty("name").GetString(), result.GetProperty("project").GetProperty("path").GetString(), result.GetProperty("project").GetProperty("opened").GetBoolean()));
        Assert.Equal(originalHash, Hash(source!));
        Assert.Equal(originalHash, Hash(imported));
        Assert.True(File.Exists(link));
        string data = Path.ChangeExtension(link, ".edb");
        Assert.True(Directory.Exists(data));
        // A35: never strip these files or attempt to open a copy lacking them.
        Assert.True(File.Exists(Path.Combine(data, "User.eod")));
        Assert.True(File.Exists(Path.Combine(data, "User.eox")));
        string[] open = [.. (await host.CallAsync("eplan_project_list", new() { ["limit"] = 200 })).GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        Assert.DoesNotContain(open, p => p.StartsWith(destination, StringComparison.OrdinalIgnoreCase));
        if (mode == "basic_project")
        {
            Assert.Equal(1, result.GetProperty("pages").GetInt32());
            Assert.StartsWith(Path.GetFileNameWithoutExtension(source) + "@", result.GetProperty("projectGuid").GetString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, result.GetProperty("pages").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("projectGuid").ValueKind);
        }

        string[] entries = [.. Directory.EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        JsonElement again = await host.ErrorAsync("eplan_project_create", Args());
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString()));
        Assert.Equal(entries, Directory.EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        string empty = Directory.CreateDirectory(Path.Combine(WriteHost.ScratchFolder, "projects", name + "_empty")).FullName;
        Dictionary<string, object?> ontoEmpty = Args();
        ontoEmpty["destination"] = empty;
        JsonElement refused = await host.ErrorAsync("eplan_project_create", ontoEmpty);
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (refused.GetProperty("reason").GetString(), refused.GetProperty("execution").GetString()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(empty));
        var records = host.AuditRecords();
        Assert.DoesNotContain(records, r => (string?)r["kind"] == "approval" && (string?)r["project"] == link);
        Assert.Contains(records, r => (string?)r["kind"] == "execution_started" && (string?)r["tool"] == "eplan_project_create" && (string?)r["project"] == link);
        Assert.Contains(records, r => (string?)r["kind"] == "execution_result" && (string?)r["project"] == link && (string?)r["execution"] == ExecutionState.Committed);
        ProjectCreateTests.Delete(destination);
        Directory.Delete(empty);
    }

    private static string Hash(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
