using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Theory]
    [InlineData("PXC.3212140", "Terminal", "XTR1", 1, "100/6/1")]
    [InlineData("SIE.5SY4106-7", "Overload", "FAH1", 0, "501/1/1")]
    [InlineData("SIE.3VA1032-2ED32-0AA0", "CircuitBreaker", "Q_3LI", 6, null)]
    [InlineData("PXC.3211813", "Terminal", null, 0, null)]
    [InlineData("PXC.2277019", "Converter", "LSW2C", 0, null)]
    public async Task PartPriority_RetainsTemplateDefinitionAndChosenSymbol_OneUndoRestores(
        string part, string category, string? symbol, int variant, string? definition)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project, page = await NewPageAsync(project);
        bool terminal = category == "Terminal";
        string stem = terminal ? "+A1-XQ10" : "=GAA+A1-QP9";
        string tag = terminal ? stem + ":1" : stem;
        await RegisterPartReadbackAsync();
        string before = (await session.SnapshotAsync()).Hash;
        try
        {
            var request = new Dictionary<string, object?>
            { ["project"] = project, ["page"] = page, ["part"] = part, ["x"] = 80, ["y"] = 160, ["dry_run"] = false };
            if (terminal)
            { request["terminal_strip"] = stem; request["terminal_number"] = "1"; }
            else
            { request["tag"] = stem; }
            if (symbol is not null)
            { request["library"] = "IEC_symbol"; request["symbol"] = symbol; request["symbol_variant"] = variant; }
            JsonElement result = await PartsCallAsync("eplan_device_place", request);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(tag, result.GetProperty("device").GetProperty("tag").GetString());
            JsonElement read = await ReadPlacedPartAsync(project, page, tag, category, 80, 160);
            Assert.Equal(tag, read.GetProperty("name").GetString());
            if (definition is not null)
            { Assert.Equal(definition, read.GetProperty("definition").GetString()); }
            Assert.Equal(symbol ?? "X2_2", read.GetProperty("symbol").GetProperty("name").GetString());
            Assert.Equal(variant, read.GetProperty("symbol").GetProperty("variant").GetInt32());
            Assert.Contains(read.GetProperty("parts").EnumerateArray(), p => p.GetProperty("part").GetString() == part);
            await session.UndoAsync();
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            try
            { await ReadPlacedPartAsync(project, page, tag, category, 80, 160); }
            catch (Exception error) { TestContext.Current.TestOutputHelper!.WriteLine("Part priority readback failed: " + error); }
            throw;
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private static async Task RegisterPartReadbackAsync()
    {
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build EplanMcp.Large.ConnectionHelper before running this test.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
    }

    private async Task<JsonElement> ReadPlacedPartAsync(string project, string page, string tag, string category, double x, double y)
    {
        string file = Path.Combine(host.Home, "placed-part-" + Guid.NewGuid().ToString("N") + ".json");
        string request = JsonSerializer.Serialize(new { project, page, tag, category, x, y });
        TestContext.Current.TestOutputHelper!.WriteLine("Placed function request: " + request);
        await File.WriteAllTextAsync(file, request, TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeTerminalPartReadback /REQUEST:\"" + file + "\"");
        string json = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper.WriteLine("Placed function result/readback: " + json);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), json);
        return document.RootElement.Clone();
    }
}
