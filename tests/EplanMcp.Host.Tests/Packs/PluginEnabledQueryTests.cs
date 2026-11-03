using System.Text.Json;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

public sealed partial class PluginEnableServiceTests
{
    [Fact]
    public async Task ListAndGet_ShowManifestFieldsAndExactEnabledVersion()
    {
        await InstallAsync("first", "1.0.0");
        await InstallAsync("first", "2.0.0");
        await Service(new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)).SetAsync("first", "1.0.0", true, null, Token);
        JsonElement listed = (await PackTools.PackListAsync(Packs, cancellationToken: Token)).StructuredContent!.Value.GetProperty("packs")[0].GetProperty("installed");
        Assert.False(listed[0].GetProperty("enabled_globally").GetBoolean());
        Assert.True(listed[1].GetProperty("enabled_globally").GetBoolean());
        JsonElement data = (await PackTools.PackGetAsync(Packs, _bridge, new(ToolProfile.Full), "first", "1.0.0", cancellationToken: Token)).StructuredContent!.Value;
        Assert.Equal("standard", data.GetProperty("kind").GetString());
        Assert.Equal("symbol-defaults", data.GetProperty("provides")[0].GetString());
        Assert.Equal("IEC", data.GetProperty("standards")[0].GetString());
        Assert.True(data.GetProperty("enabled_globally").GetBoolean());
    }

    [Fact]
    public async Task ListAndGet_UnreadableStatePreservesDataWithWarning()
    {
        await InstallAsync("first", "1.0.0");
        Directory.CreateDirectory(_locations.State);
        File.WriteAllText(Path.Combine(_locations.State, "plugins.json"), "{broken");
        CallToolResult list = await PackTools.PackListAsync(Packs, cancellationToken: Token);
        CallToolResult get = await PackTools.PackGetAsync(Packs, _bridge, new(ToolProfile.Full), "first", "1.0.0", cancellationToken: Token);
        foreach (CallToolResult result in new[] { list, get })
        {
            Assert.NotEqual(true, result.IsError);
            Assert.Contains("unknown", result.StructuredContent!.Value.GetProperty("hint").GetString(), StringComparison.Ordinal);
        }
    }

}
