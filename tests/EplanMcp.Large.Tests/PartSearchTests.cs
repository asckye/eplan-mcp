using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_part_search against the parts database EPLAN is configured with on the development machine (api-notes A27,
/// S24: <c>$(MD_PARTS)\EPLAN_parts.alk</c>, 464 parts, SIE.3RH2122-1HB40 with a macro). The search only reads: the
/// database's data files stay as they were, only its lock file may change (S24). The timings and the descriptions go to
/// the test output, for the questions the tool spec leaves to the Large tests (section 5).
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PartSearchTests(LiveEplan eplan)
{
    /// <summary>The folder of the parts database's data (<c>….adb</c>), when it is not found below %PUBLIC%\EPLAN\Data.</summary>
    public const string DatabaseVariable = "EPLANMCP_PARTS_DATABASE";

    private const string Relay = "SIE.3RH2122-1HB40";

    [Fact]
    public async Task PartSearch_FindsTheAuxiliaryContactorRelay_WithItsMacro()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_part_search", new() { ["query"] = "3RH21", ["language"] = "en_US", ["limit"] = 100 });
        clock.Stop();

        JsonElement relay = Assert.Single(data.GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == Relay && p.GetProperty("variant").GetString() == "1");
        Assert.True(relay.GetProperty("hasMacro").GetBoolean());

        // The database has two spaces after the comma (first Large run, 2026-10-21); the tool returns the text as it is.
        Assert.Equal(
            ("SIE", "3RH2122-1HB40", "3RH21221HB40", "ElectricalRelay", "COUPLING CONTACTOR RELAY,  2NO+2NC"),
            (relay.GetProperty("manufacturer").GetString(), relay.GetProperty("typeNumber").GetString(), relay.GetProperty("orderNumber").GetString(),
                relay.GetProperty("productGroup").GetString(), relay.GetProperty("description").GetString()));
        Assert.EndsWith(".alk", data.GetProperty("database").GetString(), StringComparison.OrdinalIgnoreCase);
        TestContext.Current.TestOutputHelper?.WriteLine($"{clock.ElapsedMilliseconds} ms; database '{data.GetProperty("database")}'; {data.GetProperty("total")} parts match 3RH21.");
    }

    [Fact]
    public async Task PartSearch_Manufacturer_IsTheWholeShortName()
    {
        JsonElement siemens = await eplan.CallAsync("eplan_part_search", new() { ["query"] = "3RH21", ["manufacturer"] = "sie", ["limit"] = 100 });
        JsonElement phoenix = await eplan.CallAsync("eplan_part_search", new() { ["query"] = "3RH21", ["manufacturer"] = "PXC", ["limit"] = 100 });
        JsonElement part = await eplan.CallAsync("eplan_part_search", new() { ["query"] = "3RH21", ["manufacturer"] = "SI", ["limit"] = 100 });

        Assert.Contains(siemens.GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == Relay);
        Assert.Equal((0, 0), (phoenix.GetProperty("total").GetInt32(), part.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task PartSearch_WithoutALanguage_ReadsADescriptionAndNamesAFallback()
    {
        // The relay's description has German and English (S24); in EPLAN's user interface language, or else in English.
        JsonElement relay = (await eplan.CallAsync("eplan_part_search", new() { ["query"] = Relay })).GetProperty("parts")[0];

        string? description = relay.GetProperty("description").GetString();
        Assert.False(string.IsNullOrEmpty(description));
        string fallback = relay.TryGetProperty("descriptionLanguage", out JsonElement language) ? language.GetString()! : "none";
        TestContext.Current.TestOutputHelper?.WriteLine($"Without a language: '{description}', fallback {fallback}.");
    }

    [Fact]
    public async Task PartSearch_ABroadQuery_ReadsTheWholeDatabaseInTime()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_part_search", new() { ["query"] = "SIE.", ["limit"] = 100 });
        clock.Stop();

        Assert.InRange(data.GetProperty("total").GetInt32(), 1, int.MaxValue);
        TestContext.Current.TestOutputHelper?.WriteLine($"{data.GetProperty("total")} parts contain 'SIE.', {clock.ElapsedMilliseconds} ms for the whole database (S24: 0.07 s for 464 parts without descriptions).");
    }

    [Fact]
    public async Task PartSearch_LeavesTheDatabasesDataFilesUnchanged()
    {
        string database = (await eplan.CallAsync("eplan_part_search", new() { ["query"] = Relay })).GetProperty("database").GetString()!;
        string folder = DataFolder(database);
        Dictionary<string, (long Size, DateTime Written)> before = Files(folder);

        await eplan.CallAsync("eplan_part_search", new() { ["query"] = "contactor", ["limit"] = 100 });
        await eplan.CallAsync("eplan_part_search", new() { ["query"] = "3RH21", ["language"] = "de_DE" });

        Dictionary<string, (long Size, DateTime Written)> after = Files(folder);
        string[] changed = [.. after.Where(f => !before.TryGetValue(f.Key, out var old) || old != f.Value).Select(f => f.Key).Concat(before.Keys.Except(after.Keys))];
        Assert.True(changed.Length == 0, "Changed by searching the parts database: " + string.Join(", ", changed));
    }

    /// <summary>
    /// The data folder of the database the tool names: <see cref="DatabaseVariable"/>, or else the one folder named like
    /// it (<c>EPLAN_parts.alk</c> links to <c>EPLAN_parts.adb</c>, A27) below %PUBLIC%\EPLAN\Data, where
    /// <c>$(MD_PARTS)</c> lies on a default installation.
    /// </summary>
    private static string DataFolder(string database)
    {
        if (Environment.GetEnvironmentVariable(DatabaseVariable) is { Length: > 0 } given)
        {
            return given;
        }

        Assert.SkipWhen(database.Contains('|', StringComparison.Ordinal), "The parts database is on SQL Server; its files cannot be compared.");
        string name = Path.GetFileNameWithoutExtension(database.Split('\\').Last()) + ".adb";
        string data = Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", "EPLAN", "Data");
        string[] found = Directory.Exists(data) ? Directory.GetDirectories(data, name, SearchOption.AllDirectories) : [];
        Assert.SkipUnless(found.Length == 1, $"Found {found.Length} folders {name} below {data}; set {DatabaseVariable} to the parts database's data folder.");
        return found[0];
    }

    /// <summary>Every file of the data folder but the lock file, which EPLAN rewrites on any access (S24).</summary>
    private static Dictionary<string, (long Size, DateTime Written)> Files(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Extension.Equals(".lck", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetRelativePath(folder, f.FullName), f => (f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
}
