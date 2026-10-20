namespace EplanMcp.Large.Tests;

/// <summary>
/// Reading never writes the project (architecture 8.5): every read tool leaves the fixture's files as they were. A12
/// checked this once by hand; this test keeps checking it, including the reads S6 could only check inside an action
/// (eplan_check_list, eplan_message_list).
/// </summary>
[Trait("Size", "Large")]
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
        await eplan.CallAsync("eplan_part_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_message_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_check_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_macro_list", new() { ["limit"] = 200 });
        await eplan.CallAsync("eplan_macro_get", new() { ["macro"] = MacroTests.FeedbackControl });

        Dictionary<string, (long Size, DateTime Written)> after = ProjectFiles();
        string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys))];
        Assert.True(changed.Length == 0, "Changed by reading: " + string.Join(", ", changed));
    }

    /// <summary>The project file and every file of its data directory (<c>.elk</c> and <c>.edb</c>), by relative path.</summary>
    private Dictionary<string, (long Size, DateTime Written)> ProjectFiles()
    {
        string root = Path.GetDirectoryName(eplan.ProjectPath)!;
        IEnumerable<FileInfo> files = new DirectoryInfo(eplan.ProjectPath + ".edb").EnumerateFiles("*", SearchOption.AllDirectories)
            .Append(new FileInfo(eplan.ProjectPath + ".elk"));
        return files.ToDictionary(f => Path.GetRelativePath(root, f.FullName), f => (f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
    }
}
