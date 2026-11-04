using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Fact]
    public async Task Preview_LanguageAlreadyCheckedByBridgeDoesNotRequireHostEvidence()
    {
        await WithClient(async (client, harness, store) =>
        {
            store.DisplayLanguage = "en_US";
            var args = Args();
            args["dry_run"] = true;
            args["items"] = new[] { new { kind = "text", position = (double[])[20, 20], height_mm = 3.5, contents = new Dictionary<string, string> { ["en_US"] = "Panel" } } };
            JsonElement data = WriteToolHarness.Structured(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ExecutionState.NotStarted, data.GetProperty("execution").GetString());
            Assert.Single(harness.Bridge.Calls, call => call.Operation == OperationNames.PlacementTextCreatePlan);
            Assert.Empty(store.Items);
        });
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("refuse", false, false)]
    [InlineData("warn", false, true)]
    [InlineData("warn", true, true)]
    public async Task MissingDisplayLanguage_WarnPreservesGermanText(string? guard, bool preview, bool accepted)
    {
        await WithClient(async (client, harness, store) =>
        {
            store.DisplayLanguage = "en_US";
            var args = Args();
            args["dry_run"] = preview;
            if (guard is not null)
            { args["layout_guard"] = guard; }
            args["items"] = new[] { new { kind = "text", position = (double[])[20, 20], height_mm = 3.5, contents = new Dictionary<string, string> { ["de_DE"] = "Eingang" } } };
            var call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.Equal(!accepted, call.IsError == true);
            if (accepted)
            {
                JsonElement data = WriteToolHarness.Structured(call);
                Assert.Contains("project display language", data.GetProperty("warning").GetString(), StringComparison.Ordinal);
                Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, data.GetProperty("execution").GetString());
            }
            Assert.Equal(accepted && !preview ? 1 : 0, store.Items.Count);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwentyLanguages_KeepPerLanguageLimitThroughProtocol(bool oversized)
    {
        await WithClient(async (client, harness, store) =>
        {
            var args = Args();
            args["response_format"] = "detailed";
            args["items"] = new[] { new { kind = "text", position = (double[])[20, 20], height_mm = 3.5, contents = PlacementTextLanguageTests.Contents(oversized ? 501 : 40) } };
            var call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.Equal(oversized, call.IsError == true);
            if (!oversized)
            { Assert.Equal(20, Assert.Single(store.Items).Contents.Count); }
            else
            { Assert.Empty(harness.Bridge.Calls); Assert.Empty(store.Items); }
        });
    }

}
