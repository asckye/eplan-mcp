using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The exports of a whole project on the fixture (api-notes A25, S25 on a copy of the sample project): each leaves its
/// file in its new folder and nowhere else, and the fixture's design data stays as it was; only the <c>User.*</c> files
/// may change (A25), so <see cref="NoSideEffectTests"/> still holds for the reads. Each test deletes the folder it got
/// once checked: a PXF of the sample project is about 600 MB.
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectExportTests(LiveEplan eplan)
{
    [Fact]
    public async Task Pxf_IsOneEpjFileInItsFolderOnly_AndTheProjectIsUnchanged()
    {
        // The name ends with .epj: the tool leaves the extension to EPLAN, so it must not come out as .epj.epj.
        (JsonElement data, long milliseconds) = await ExportAsync("eplan_export_pxf", new() { ["file_name"] = "LargePxf.epj" });

        Assert.False(data.GetProperty("masterData").GetBoolean());
        Assert.Equal(("LargePxf.epj", 1), (data.GetProperty("files")[0].GetProperty("path").GetString(), data.GetProperty("fileCount").GetInt32()));
        Assert.InRange(data.GetProperty("totalBytes").GetInt64(), 100L * 1024 * 1024, 2L * 1024 * 1024 * 1024);
        TestContext.Current.TestOutputHelper?.WriteLine($"PXF without master data: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S25: 19.2 s, 598 MB).");
    }

    [Fact]
    public async Task Parts_IsOneXmlFileInItsFolderOnly_AndEplanStillAnswers()
    {
        // Which fields the XML gives each part decides how the tool describes the file (tool spec 5).
        (JsonElement data, long milliseconds) = await ExportAsync("eplan_export_parts", new() { ["file_name"] = "LargeParts" }, directory => Head(Path.Combine(directory, "LargeParts.xml")));

        Assert.Equal(("LargeParts.xml", 1), (data.GetProperty("files")[0].GetProperty("path").GetString(), data.GetProperty("fileCount").GetInt32()));
        Assert.InRange(data.GetProperty("totalBytes").GetInt64(), 1024L, 200L * 1024 * 1024);
        TestContext.Current.TestOutputHelper?.WriteLine($"Parts list: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S25: 61.9 s, 14 MB).");
    }

    [Theory]
    [InlineData("Device tag list", "LargeLabels.txt")]
    [InlineData("Terminal diagram", "LargeLabels.xlsx")]
    public async Task Labels_AreOneFileWithTheSchemesExtension_InItsFolderOnly(string scheme, string file)
    {
        // S25: the device tag list is fixed-width text of about 40 KB, the terminal diagram an xlsx of about 805 KB. That
        // the format is read from the scheme's Data.File.Format is what this checks first (tool spec 5).
        (JsonElement data, long milliseconds) = await ExportAsync(
            "eplan_export_labels",
            new() { ["scheme"] = scheme, ["language"] = "en_US", ["file_name"] = "LargeLabels" },
            directory =>
            {
                if (file.EndsWith(".txt", StringComparison.Ordinal))
                {
                    Head(Path.Combine(directory, file));
                }
            });

        Assert.Equal((scheme, file, 1), (data.GetProperty("scheme").GetString(), data.GetProperty("files")[0].GetProperty("path").GetString(), data.GetProperty("fileCount").GetInt32()));
        Assert.InRange(data.GetProperty("totalBytes").GetInt64(), 1L, 50L * 1024 * 1024);
        TestContext.Current.TestOutputHelper?.WriteLine($"Labels with '{scheme}': {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S25: about 2 s).");
    }

    [Fact]
    public async Task Labels_AnUnknownScheme_ListsTheSchemesThereAre_AndExportsNothing()
    {
        string exports = Path.TrimEndingDirectorySeparator(HostDataLocations.FromEnvironment().Exports);
        string[] before = ExportFolders(exports);

        JsonElement error = await eplan.ErrorAsync("eplan_export_labels", new() { ["scheme"] = "No such labelling scheme" });

        string candidates = error.GetProperty("metadata").GetProperty("candidates").GetString()!;
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("Device tag list", candidates.Split('\n'));
        Assert.DoesNotContain("InternalRefDefScheme", candidates.Split('\n'));
        Assert.Equal(before, ExportFolders(exports));
        TestContext.Current.TestOutputHelper?.WriteLine("Labelling schemes: " + candidates.Replace('\n', ',') + ".");
    }

    /// <summary>
    /// Runs one export and checks what every export of a whole project must leave: its files in a new folder below the
    /// export root and nowhere else, no new export folder besides it, no new file next to the fixture, the fixture's
    /// design data unchanged, and EPLAN still answering (S25 saw EPLAN stop after a parts list export, A25). Deletes the
    /// folder before returning.
    /// </summary>
    private async Task<(JsonElement Data, long Milliseconds)> ExportAsync(string tool, Dictionary<string, object?> args, Action<string>? inspect = null)
    {
        string exports = Path.TrimEndingDirectorySeparator(HostDataLocations.FromEnvironment().Exports);
        string fixtureFolder = Path.GetDirectoryName(eplan.ProjectPath)!;
        string[] foldersBefore = ExportFolders(exports);
        string[] besideBefore = Directory.GetFileSystemEntries(fixtureFolder);
        Dictionary<string, (long Size, DateTime Written)> designBefore = NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: false);

        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync(tool, args);
        clock.Stop();
        string directory = data.GetProperty("directory").GetString()!;
        try
        {
            Assert.Equal(LiveEplan.ProjectName, data.GetProperty("project").GetString());
            Assert.StartsWith(Path.Combine(exports, LiveEplan.ProjectName) + Path.DirectorySeparatorChar, directory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([Path.TrimEndingDirectorySeparator(directory)], ExportFolders(exports).Except(foldersBefore, StringComparer.OrdinalIgnoreCase));
            Assert.Equal(besideBefore.Order(StringComparer.OrdinalIgnoreCase), Directory.GetFileSystemEntries(fixtureFolder).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(
                data.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()!).Order(StringComparer.OrdinalIgnoreCase),
                Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(directory, f)).Order(StringComparer.OrdinalIgnoreCase));
            Dictionary<string, (long Size, DateTime Written)> designAfter = NoSideEffectTests.ProjectFiles(eplan.ProjectPath, withUserData: false);
            string[] changed = [.. designAfter.Where(f => !designBefore.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(designBefore.Keys.Except(designAfter.Keys))];
            Assert.True(changed.Length == 0, $"Changed by {tool}: " + string.Join(", ", changed));
            Assert.True((await eplan.CallAsync("eplan_session_status")).GetProperty("connected").GetBoolean());
            inspect?.Invoke(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return (data, clock.ElapsedMilliseconds);
    }

    /// <summary>Writes the first 3000 characters of an exported text file to the test output.</summary>
    private static void Head(string file)
    {
        using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
        char[] buffer = new char[3000];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        TestContext.Current.TestOutputHelper?.WriteLine($"{Path.GetFileName(file)} begins:{Environment.NewLine}{new string(buffer, 0, read)}");
    }

    /// <summary>Every export folder, <c>exports\&lt;project&gt;\&lt;time&gt;-&lt;suffix&gt;</c>.</summary>
    private static string[] ExportFolders(string exports) =>
        Directory.Exists(exports) ? [.. Directory.GetDirectories(exports).SelectMany(Directory.GetDirectories)] : [];
}
