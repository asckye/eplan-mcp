using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using EplanMcp.Contracts;
using EplanMcp.Host;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_part_export against the parts database EPLAN is configured with on the development machine (api-notes A27,
/// A30; S24 r11 exported a restored copy of it: 464 parts, XML 2 s and 5 MB, EDZ 44 s and 128 MB). The export only
/// reads the database, so each export is checked against SHA-256 hashes of the database's data files before and after
/// (tool spec 5): the <c>.alk</c> link and every file of the <c>.adb</c> folder but the lock files, which EPLAN rewrites
/// on any access (A27), and <c>User.eod</c>/<c>User.eox</c>, EPLAN's per-user session record, rewritten when EPLAN
/// closes (A30; the first run saw them change, the part data not). Each test deletes the folder it got.
/// </summary>
[Trait("Size", "Large")]
public sealed class PartExportTests(LiveEplan eplan)
{
    /// <summary>Set to 1 to run the EDZ export too, before a release only: 44 s and 128 MB for the sample database (tool spec 7).</summary>
    public const string EdzVariable = "EPLANMCP_LARGE_EDZ";

    private const string Tool = "eplan_part_export";
    private const string Relay = "SIE.3RH2122-1HB40";
    private const string NoSuchPart = "EPLANMCP.NO-SUCH-PART";

    [Fact]
    public async Task Xml_EveryPart_IsOneFile_WhoseCountIsThePartCount_AndTheDatabaseIsUnchanged()
    {
        PartsXml xml = default;
        (JsonElement data, long milliseconds) = await ExportAsync(new() { ["file_name"] = "LargePartsDb" }, "LargePartsDb.xml", file => xml = PartsXml.Read(file));

        // The first run: the root counts 454 parts, MDPartsDatabase.Parts lists 464 with the variants (tool spec 5).
        int count = data.GetProperty("partCount").GetInt32();
        int variants = data.GetProperty("variantCount").GetInt32();
        Assert.Equal("partsmanagement", xml.Root);
        Assert.Equal(count.ToString(CultureInfo.InvariantCulture), xml.Count);
        Assert.InRange(count, 1, variants);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"XML of every part: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes, partCount {count}, variantCount {variants}, {xml.Parts} part elements with {xml.DistinctPartNumbers} distinct part numbers, first {string.Join(", ", xml.PartNumbers)} (S24 r11: 2.05 s, 5.0 MB).");
    }

    [Fact]
    public async Task Xml_OnePart_IsThatPartOnly()
    {
        PartsXml xml = default;
        (JsonElement data, long milliseconds) = await ExportAsync(new() { ["parts"] = new[] { Relay }, ["file_name"] = "LargeRelay" }, "LargeRelay.xml", file => xml = PartsXml.Read(file));

        Assert.Equal((1, 1), (data.GetProperty("partCount").GetInt32(), data.GetProperty("variantCount").GetInt32()));
        Assert.Equal(("partsmanagement", "1", 1), (xml.Root, xml.Count, xml.Parts));
        Assert.Equal([Relay], xml.PartNumbers);
        TestContext.Current.TestOutputHelper?.WriteLine($"XML of {Relay}: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes (S24 r11: 4 KB for one part).");
    }

    [Fact]
    public async Task APartNumberTheDatabaseDoesNotHave_IsPartNotFound_AndLeavesNoFolder()
    {
        string exports = Path.TrimEndingDirectorySeparator(HostDataLocations.FromEnvironment().Exports);
        string[] before = ExportFolders(exports);

        JsonElement error = await eplan.ErrorAsync(Tool, new() { ["parts"] = new[] { Relay, NoSuchPart } });

        JsonElement metadata = error.GetProperty("metadata");
        Assert.Equal(ErrorReason.PartNotFound, error.GetProperty("reason").GetString());
        Assert.Equal((NoSuchPart, "1"), (metadata.GetProperty("missing").GetString(), metadata.GetProperty("missingCount").GetString()));
        Assert.Equal(before, ExportFolders(exports));
    }

    [Fact]
    public async Task Edz_EveryPart_IsOneFile_BeforeAReleaseOnly()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EdzVariable) == "1", $"The EDZ export takes about 44 s and 128 MB; set {EdzVariable}=1 to run it before a release.");

        (JsonElement data, long milliseconds) = await ExportAsync(new() { ["format"] = "edz", ["file_name"] = "LargePartsDb" }, "LargePartsDb.edz");

        Assert.InRange(data.GetProperty("partCount").GetInt32(), 1, data.GetProperty("variantCount").GetInt32());
        Assert.InRange(data.GetProperty("totalBytes").GetInt64(), 1024L * 1024, 2L * 1024 * 1024 * 1024);
        TestContext.Current.TestOutputHelper?.WriteLine($"EDZ of every part: {milliseconds} ms, {data.GetProperty("totalBytes")} bytes, partCount {data.GetProperty("partCount")}, variantCount {data.GetProperty("variantCount")} (S24 r11: 44 s, 128 MB).");
    }

    /// <summary>
    /// Runs one export and checks what it must leave: one file of the expected name in a new folder below
    /// <c>exports\parts-database\</c> and nowhere else, the database named as eplan_part_search names it, its data files
    /// unchanged, and EPLAN still answering. Deletes the folder before returning.
    /// </summary>
    private async Task<(JsonElement Data, long Milliseconds)> ExportAsync(Dictionary<string, object?> args, string file, Action<string>? inspect = null)
    {
        string exports = Path.TrimEndingDirectorySeparator(HostDataLocations.FromEnvironment().Exports);
        string database = (await eplan.CallAsync("eplan_part_search", new() { ["query"] = Relay })).GetProperty("database").GetString()!;
        string folder = PartSearchTests.DataFolder(database);
        string[] foldersBefore = ExportFolders(exports);
        Dictionary<string, string> before = Hashes(folder);

        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync(Tool, args);
        clock.Stop();
        string directory = data.GetProperty("directory").GetString()!;
        try
        {
            Assert.Equal(database, data.GetProperty("database").GetString());
            Assert.StartsWith(Path.Combine(exports, "parts-database") + Path.DirectorySeparatorChar, directory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([Path.TrimEndingDirectorySeparator(directory)], ExportFolders(exports).Except(foldersBefore, StringComparer.OrdinalIgnoreCase));
            Assert.Equal([file], Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(directory, f)));
            Assert.Equal((file, 1), (data.GetProperty("files")[0].GetProperty("path").GetString(), data.GetProperty("fileCount").GetInt32()));
            inspect?.Invoke(Path.Combine(directory, file));
            AssertUnchanged(before, Hashes(folder));
            Assert.True((await eplan.CallAsync("eplan_session_status")).GetProperty("connected").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return (data, clock.ElapsedMilliseconds);
    }

    /// <summary>
    /// SHA-256 of the <c>.alk</c> link next to <paramref name="folder"/> and of every file in it, by path, but the lock
    /// files and EPLAN's session record <c>User.eod</c>/<c>User.eox</c> (A30).
    /// </summary>
    internal static Dictionary<string, string> Hashes(string folder)
    {
        string[] session = ["User.eod", "User.eox"];
        IEnumerable<string> files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) && !session.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .Append(Path.ChangeExtension(folder, ".alk"))
            .Where(File.Exists);
        return files.ToDictionary(f => f, Sha256, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The file's SHA-256, read while EPLAN may have it open.</summary>
    internal static string Sha256(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void AssertUnchanged(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out string? old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase))];
        Assert.True(changed.Length == 0, "Changed by the export: " + string.Join(", ", changed));
        Assert.NotEmpty(before);
    }

    /// <summary>Every export folder, <c>exports\&lt;project or database&gt;\&lt;time&gt;-&lt;suffix&gt;</c>.</summary>
    internal static string[] ExportFolders(string exports) =>
        Directory.Exists(exports) ? [.. Directory.GetDirectories(exports).SelectMany(Directory.GetDirectories)] : [];

    /// <summary>
    /// What S24 r13 read of a parts XML file: the root element <c>partsmanagement</c> with <c>count</c>, and one
    /// <c>part</c> element per part with the part number in <c>P_ARTICLE_PARTNR</c> (the distinct ones counted, the first five kept).
    /// </summary>
    private readonly record struct PartsXml(string Root, string? Count, int Parts, int DistinctPartNumbers, List<string> PartNumbers)
    {
        public static PartsXml Read(string file)
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            reader.MoveToContent();
            string root = reader.LocalName;
            string? count = reader.GetAttribute("count");
            int parts = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var numbers = new List<string>();
            while (reader.Read())
            {
                if (reader is { NodeType: XmlNodeType.Element, Depth: 1, LocalName: "part" })
                {
                    parts++;
                    if (reader.GetAttribute("P_ARTICLE_PARTNR") is { } number && distinct.Add(number) && numbers.Count < 5)
                    {
                        numbers.Add(number);
                    }
                }
            }

            return new PartsXml(root, count, parts, distinct.Count, numbers);
        }
    }
}
