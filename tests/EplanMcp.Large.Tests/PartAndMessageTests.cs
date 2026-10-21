using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_part_list, the multi-language fallback, messages, checks and the session tools on the live system.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PartAndMessageTests(LiveEplan eplan)
{
    private const string MotorBreaker = "SIE.3RV2011-1BA25";

    [Fact]
    public async Task PartList_CountsThePartsPerManufacturer()
    {
        JsonElement all = await eplan.CallAsync("eplan_part_list", new() { ["limit"] = 1 });
        JsonElement siemens = await eplan.CallAsync("eplan_part_list", new() { ["manufacturer"] = "SIE", ["limit"] = 1 });
        JsonElement phoenix = await eplan.CallAsync("eplan_part_list", new() { ["manufacturer"] = "PXC", ["limit"] = 1 });

        Assert.Equal((314, 33, 23), (all.GetProperty("total").GetInt32(), siemens.GetProperty("total").GetInt32(), phoenix.GetProperty("total").GetInt32()));
    }

    [Theory]
    [InlineData("en_US", "CIRCUIT-BREAKER SPRING-L. CONN. 2A")]
    [InlineData("de_DE", "LEISTUNGSSCHALTER FEDERZUGANSCHL. 2A")]
    [InlineData("zh_CN", null)]
    public async Task PartList_GivenALanguage_ReadsThatLanguageOnly(string language, string? expected)
    {
        JsonElement part = await Part(language);

        Assert.Equal(expected, part.GetProperty("description").GetString());
        Assert.False(part.TryGetProperty("descriptionLanguage", out _));
    }

    [Fact]
    public async Task PartList_WithoutALanguage_FallsBackAndNamesTheLanguage()
    {
        // The part has German and English descriptions only (api-notes A12). With EPLAN in either of those, the text
        // comes without a fallback; in any other language, it falls back to English.
        JsonElement part = await Part(language: null);

        string? description = part.GetProperty("description").GetString();
        if (part.TryGetProperty("descriptionLanguage", out JsonElement language))
        {
            Assert.Equal(("en_US", "CIRCUIT-BREAKER SPRING-L. CONN. 2A"), (language.GetString(), description));
        }
        else
        {
            Assert.True(description is "CIRCUIT-BREAKER SPRING-L. CONN. 2A" or "LEISTUNGSSCHALTER FEDERZUGANSCHL. 2A", description);
        }
    }

    [Fact]
    public async Task MessageAndCheckList_ReadWithoutErrors()
    {
        JsonElement messages = await eplan.CallAsync("eplan_message_list", new() { ["limit"] = 50 });
        JsonElement checks = await eplan.CallAsync("eplan_check_list", new() { ["limit"] = 50 });

        Assert.InRange(messages.GetProperty("messages").GetArrayLength(), 0, messages.GetProperty("total").GetInt32());
        JsonElement counts = checks.GetProperty("counts");
        int counted = counts.GetProperty("error").GetInt32() + counts.GetProperty("warning").GetInt32() + counts.GetProperty("hint").GetInt32();
        Assert.InRange(counted, 0, checks.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SessionSelect_SelectsTheRunningEplan()
    {
        JsonElement selected = await eplan.CallAsync("eplan_session_select", new() { ["process_id"] = eplan.ProcessId });
        JsonElement status = await eplan.CallAsync("eplan_session_status");

        Assert.Equal(eplan.ProcessId, selected.GetProperty("selected").GetProperty("processId").GetInt32());
        Assert.Contains(status.GetProperty("instances").EnumerateArray(), i => i.GetProperty("processId").GetInt32() == eplan.ProcessId && i.GetProperty("selected").GetBoolean());
        Assert.True(status.GetProperty("eplan").GetProperty("apiExtensionLicence").GetBoolean());
    }

    private async Task<JsonElement> Part(string? language)
    {
        Dictionary<string, object?> args = new() { ["text"] = MotorBreaker };
        if (language is not null)
        {
            args["language"] = language;
        }

        return Assert.Single((await eplan.CallAsync("eplan_part_list", args)).GetProperty("parts").EnumerateArray());
    }
}
