using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Host;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_translation_export against the translation database EPLAN is configured with on the development machine
/// (api-notes A27, A30; S24 r11 exported it: <c>$(md_translate)\Translate.db</c>, XML 0.68 s and 2.8 MB, tab-separated
/// 0.76 s and 2.1 MB, the database unchanged). Each export is checked against the database file's SHA-256 before and
/// after. The tab-separated file's first line goes to the test output: its columns are for the Large test to find out
/// (tool spec 5). Each test deletes the folder it got.
/// </summary>
[Trait("Size", "Large")]
public sealed class TranslationExportTests(LiveEplan eplan)
{
    /// <summary>The translation database's file, when it is not found below %PUBLIC%\EPLAN\Data.</summary>
    public const string DatabaseVariable = "EPLANMCP_TRANSLATION_DATABASE";

    private const string Tool = "eplan_translation_export";

    [Fact]
    public async Task Tab_IsOneTextFile_AndTheDatabaseIsUnchanged()
    {
        string header = string.Empty;
        (JsonElement data, long milliseconds) = await ExportAsync(
            new() { ["file_name"] = "LargeTranslations" },
            "LargeTranslations.txt",
            file =>
            {
                using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
                header = reader.ReadLine() ?? string.Empty;
                TestContext.Current.TestOutputHelper?.WriteLine($"Encoding {reader.CurrentEncoding.WebName}; first line: {header}");
                TestContext.Current.TestOutputHelper?.WriteLine("Second line: " + reader.ReadLine());
            });

        Assert.Contains('\t', header);
        TestContext.Current.TestOutputHelper?.WriteLine($"Tab-separated: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S24 r11: 0.76 s, 2.1 MB).");
    }

    [Fact]
    public async Task Xml_IsOneXmlFile_AndTheDatabaseIsUnchanged()
    {
        (JsonElement data, long milliseconds) = await ExportAsync(
            new() { ["format"] = "xml", ["file_name"] = "LargeTranslations" },
            "LargeTranslations.xml",
            file =>
            {
                using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
                char[] buffer = new char[3000];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                TestContext.Current.TestOutputHelper?.WriteLine($"LargeTranslations.xml begins:{Environment.NewLine}{new string(buffer, 0, read)}");
            });

        TestContext.Current.TestOutputHelper?.WriteLine($"XML: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S24 r11: 0.68 s, 2.8 MB).");
    }

    /// <summary>
    /// Runs one export and checks what it must leave: one non-empty file of the expected name in a new folder below
    /// <c>exports\translation-database\</c> and nowhere else, the database file's hash unchanged, and EPLAN still
    /// answering. Deletes the folder before returning.
    /// </summary>
    private async Task<(JsonElement Data, long Milliseconds)> ExportAsync(Dictionary<string, object?> args, string file, Action<string> inspect)
    {
        string exports = Path.TrimEndingDirectorySeparator(HostDataLocations.FromEnvironment().Exports);
        string[] foldersBefore = PartExportTests.ExportFolders(exports);
        string databaseFile = DatabaseFile();
        string before = PartExportTests.Sha256(databaseFile);

        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync(Tool, args);
        clock.Stop();
        string directory = data.GetProperty("directory").GetString()!;
        try
        {
            string database = data.GetProperty("database").GetString()!;
            Assert.True(
                database.EndsWith("\\" + Path.GetFileName(databaseFile), StringComparison.OrdinalIgnoreCase),
                $"EPLAN uses '{database}', not the file hashed, {databaseFile}; set {DatabaseVariable} to its file.");
            Assert.StartsWith(Path.Combine(exports, "translation-database") + Path.DirectorySeparatorChar, directory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([Path.TrimEndingDirectorySeparator(directory)], PartExportTests.ExportFolders(exports).Except(foldersBefore, StringComparer.OrdinalIgnoreCase));
            Assert.Equal([file], Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(directory, f)));
            Assert.InRange(data.GetProperty("totalBytes").GetInt64(), 1L, long.MaxValue);
            inspect(Path.Combine(directory, file));
            Assert.True((await eplan.CallAsync("eplan_session_status")).GetProperty("connected").GetBoolean());
            Assert.True(before == PartExportTests.Sha256(databaseFile), "The export changed the translation database " + databaseFile);
            TestContext.Current.TestOutputHelper?.WriteLine($"Database '{database}'.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return (data, clock.ElapsedMilliseconds);
    }

    /// <summary>
    /// The translation database's file: <see cref="DatabaseVariable"/>, or else the one <c>Translate.db</c> below
    /// %PUBLIC%\EPLAN\Data, where <c>$(md_translate)</c> lies on a default installation (A27). The export then checks
    /// that EPLAN uses a file of that name.
    /// </summary>
    private static string DatabaseFile()
    {
        if (Environment.GetEnvironmentVariable(DatabaseVariable) is { Length: > 0 } given)
        {
            return given;
        }

        const string name = "Translate.db";
        string data = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", "EPLAN", "Data");
        string[] found = Directory.Exists(data) ? Directory.GetFiles(data, name, SearchOption.AllDirectories) : [];
        Assert.SkipUnless(found.Length == 1, $"Found {found.Length} files {name} below {data}; set {DatabaseVariable} to the translation database's file.");
        return found[0];
    }
}
