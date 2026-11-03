using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Full property reads (response_format detailed) of the same objects, many times: the 2026-11-02 crash
/// (AccessViolationException in PropertyValue.UnsafeRemoteValue) came from a full read whose owner was finalized on
/// the finalizer thread during the read (api-notes A80). Start the EPLAN process or offline host with a small
/// COMPlus_GCgen0size (for example 0x10000) to make that race likely; without it the test only checks stable results.
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class FullPropertyReadTests(LiveEplan eplan)
{
    private const string Page = "=GAA+A1&EFS1/1";

    [Fact]
    public async Task DetailedReads_RepeatedOnOnePage_StayTheSame()
    {
        JsonElement listed = await eplan.CallAsync("eplan_device_list", new() { ["page"] = Page, ["limit"] = 200 });
        // The list also names bare structures such as =GAA+A1, which eplan_device_get does not read.
        string[] tags = [.. listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!).Where(t => t.Contains('-', StringComparison.Ordinal))];
        Assert.NotEmpty(tags);
        string[] expected = await RoundAsync();
        for (int round = 1; round < 20; round++)
        {
            string[] actual = await RoundAsync();
            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    TestContext.Current.TestOutputHelper?.WriteLine($"Round {round}, read {i}:\n{expected[i]}\n{actual[i]}");
                }

                Assert.True(actual[i] == expected[i], $"Round {round} read {i} differs from round 0; see the test output.");
            }
        }

        async Task<string[]> RoundAsync()
        {
            // Read, not compared: the project carries a time stamp that changes between reads.
            await eplan.CallAsync("eplan_project_get", new() { ["language"] = "en_US", ["response_format"] = "detailed" });
            var results = new List<string>
            {
                (await eplan.CallAsync("eplan_page_get", new() { ["page"] = Page, ["language"] = "en_US", ["response_format"] = "detailed" })).GetRawText(),
            };
            foreach (string tag in tags)
            {
                results.Add((await eplan.CallAsync("eplan_device_get", new() { ["tag"] = tag, ["language"] = "en_US", ["response_format"] = "detailed" })).GetRawText());
            }

            return [.. results];
        }
    }
}
