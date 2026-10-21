namespace EplanMcp.Large.Tests;

/// <summary>
/// Reading never writes the project (architecture 8.5): every read tool leaves the fixture's files as they were. A12
/// checked this once by hand; this test keeps checking it, including the reads S6 could only check inside an action
/// (eplan_check_list, eplan_message_list).
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class NoSideEffectTests(LiveEplan eplan)
{
    [Fact]
    public async Task EveryReadTool_LeavesTheProjectFilesUnchanged()
    {
        Dictionary<string, (long Size, DateTime Written)> before = ProjectFiles();

        await eplan.CallAsync("eplan_project_get", new() { ["response_format"] = "detailed" });
        await eplan.CallAsync("eplan_page_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_page_get", new() { ["page"] = "=GAA+A1&EFS1/1", ["response_format"] = "detailed" });
        await eplan.CallAsync("eplan_device_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_device_get", new() { ["tag"] = "=GAA+A1-FC1", ["response_format"] = "detailed" });
        await eplan.CallAsync("eplan_connection_list", new() { ["page"] = "=GAA+A1&EFS1/1", ["limit"] = 200 });
        await eplan.CallAsync("eplan_cable_get", new() { ["cable"] = "+B1-WG2" });
        await eplan.CallAsync("eplan_terminal_strip_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_part_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_message_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_check_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_macro_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_macro_get", new() { ["macro"] = MacroTests.FeedbackControl });

        Dictionary<string, (long Size, DateTime Written)> after = ProjectFiles();
        string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys))];
        Assert.True(changed.Length == 0, "Changed by reading: " + string.Join(", ", changed));
    }

    private Dictionary<string, (long Size, DateTime Written)> ProjectFiles() => ProjectFiles(eplan.ProjectPath, withUserData: true);

    /// <summary>
    /// The project file and every file of its data directory (<c>.elk</c> and <c>.edb</c>), by relative path. Without
    /// user data, the <c>User.*</c> files are left out: EPLAN rewrites them for the current project on any call (A17).
    /// </summary>
    internal static Dictionary<string, (long Size, DateTime Written)> ProjectFiles(string projectPath, bool withUserData)
    {
        string root = Path.GetDirectoryName(projectPath)!;
        IEnumerable<FileInfo> files = new DirectoryInfo(projectPath + ".edb").EnumerateFiles("*", SearchOption.AllDirectories)
            .Append(new FileInfo(projectPath + ".elk"))
            .Where(f => withUserData || !f.Name.StartsWith("User.", StringComparison.OrdinalIgnoreCase));
        return files.ToDictionary(f => Path.GetRelativePath(root, f.FullName), f => (f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
    }
}
