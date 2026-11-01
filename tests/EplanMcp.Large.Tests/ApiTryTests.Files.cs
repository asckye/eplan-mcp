using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.DependencyInjection;

namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    [Fact]
    public async Task ScratchCopy_FileImportOneUndoRestoresDeviceListAndEarlierHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string sentinel = Assert.Single(await selection.CreatePagesAsync(1));
        Directory.CreateDirectory(Path.Combine(_home, "imports"));
        string source = Path.Combine(_home, "imports", "devices.xml");
        string unique = Guid.NewGuid().ToString("N");
        string sample = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "s34-devices.xml"), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(source, sample.Replace("10 20 206", "ApiTryA" + unique, StringComparison.Ordinal).Replace("12 22 126", "ApiTryB" + unique, StringComparison.Ordinal), TestContext.Current.CancellationToken);
        string hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken)));
        IBridgeClient bridge = _services!.GetRequiredService<IBridgeClient>();
        async Task<int> Entries()
        {
            WritePlan plan = await bridge.InvokeAsync<ImportListArgs, WritePlan>(OperationNames.ImportListPlan,
                new() { Project = selection.Project, Kind = "devices", File = "devices.xml", FilePath = source, Sha256 = hash, Format = "xml" }, TestContext.Current.CancellationToken);
            return JsonDocument.Parse(plan.Details!["before"]).RootElement.GetProperty("deviceListEntries").GetInt32();
        }
        int before = await Entries();
        string state = await selection.StateAsync();
        JsonElement target = await Locator(selection.Project, "Project");
        JsonElement written = await Call(selection.Project, "O:Eplan.EplApi.HEServices.DeviceService.ImportDeviceList",
            new { target, oProject = target, strImportFilePath = "devices.xml", strConverter = "XDLXmlExporter" });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Empty(written.GetProperty("files").EnumerateArray());
        Assert.Equal(before + 2, await Entries());
        await selection.UndoAsync();
        Assert.Equal(before, await Entries());
        Assert.Equal(state, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
    }
}
