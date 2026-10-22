using System.Text.Json;
using EplanMcp.Host;
using EplanMcp.Host.Exports;

namespace EplanMcp.Large.Tests;

/// <summary>The mandatory in-transaction conflict guard on LargeWrite, with the S34 Phoenix1 self-export (A39).</summary>
[Trait("Size", "Large")]
public sealed class ImportPlcTests : IClassFixture<DestructiveHost>
{
    private const string Tool = "eplan_import_plc";
    private readonly DestructiveHost _host;

    public ImportPlcTests(LiveEplan eplan, DestructiveHost host)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        host.Approvals.Forget();
        _host = host;
    }

    [Fact]
    public async Task SelfExport_ConflictsInsideTheTransaction_AndRestoresAllBusinessCounts()
    {
        // There is no PLC bus-data export tool; S34's self-export also belongs to the sample project.
        string? source = Environment.GetEnvironmentVariable("EPLANMCP_LARGE_PLC_PBF");
        if (string.IsNullOrWhiteSpace(source))
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "spikes", "s34");
            source = Directory.Exists(root) ? Directory.EnumerateFiles(root, "roundtrip.pbf", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "export-00", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        }

        Assert.SkipWhen(string.IsNullOrWhiteSpace(source), "Set EPLANMCP_LARGE_PLC_PBF to the Phoenix1 .pbf self-export from LargeWrite, or retain S34's export-00/roundtrip.pbf under ~/EplanMcp/scratch/spikes/s34; no PLC bus-data export tool is available.");
        Assert.True(File.Exists(source), "The PLC self-export must be an existing .pbf from LargeWrite or S34's sample project.");
        Assert.EndsWith(".pbf", source, StringComparison.OrdinalIgnoreCase);
        string imports = new HostDataLocations(_host.Home).Imports;
        Directory.CreateDirectory(imports);
        File.Copy(source, Path.Combine(imports, "Phoenix1.pbf"));
        Dictionary<string, object?> args = new() { ["project"] = _host.Writable, ["file"] = "Phoenix1.pbf", ["converter"] = "PlcDcXMLExchangerUniversal" };

        JsonElement planned = await _host.CallAsync(Tool, new(args) { ["dry_run"] = true });
        Assert.False(planned.GetProperty("preflight").GetProperty("complete").GetBoolean());
        Assert.True(planned.GetProperty("preflight").GetProperty("canExecute").GetBoolean());
        JsonElement before = planned.GetProperty("before");
        Assert.InRange(before.GetProperty("functions").GetInt32(), 1, int.MaxValue);
        Assert.InRange(before.GetProperty("plcBoxes").GetInt32(), 1, int.MaxValue);
        long bytes = planned.GetProperty("bytes").GetInt64();
        string hash = planned.GetProperty("sha256").GetString()!;
        string description = $"Phoenix1.pbf ({ByteSize.Format(bytes)}, sha256 {hash[..12]}…); file preflight is incomplete; new object names will be checked against pre-import full names inside the transaction; any match rolls the import back.";
        _host.Approvals.Approve(Tool, _host.Writable, ["import PLC bus data: " + description]);

        JsonElement error = await _host.ErrorAsync(Tool, args);
        JsonElement after = (await _host.CallAsync(Tool, new(args) { ["dry_run"] = true })).GetProperty("before");

        Assert.Equal(("ALREADY_EXISTS", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(before.GetRawText(), after.GetRawText());
        JsonElement metadata = error.GetProperty("metadata");
        Assert.True(File.Exists(metadata.GetProperty("backup").GetString()), "The project backup must be retained after rollback.");
        using JsonDocument duplicates = JsonDocument.Parse(metadata.GetProperty("duplicates").GetString()!);
        string[] names = [.. duplicates.RootElement.EnumerateArray().Select(d => d.GetProperty("name").GetString()!)];
        Assert.InRange(names.Length, 1, 50);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("=F+A2-KE1", names);
        int total = int.Parse(metadata.GetProperty("duplicateCount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(total >= names.Length);
        if (total > names.Length)
        {
            Assert.Equal("true", metadata.GetProperty("truncated").GetString());
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"Before/after: {before}; S34 fresh baseline: 2,876 functions, 20 PLC boxes, 2,900 article references. Distinct conflicting names: {total}.");
    }
}
