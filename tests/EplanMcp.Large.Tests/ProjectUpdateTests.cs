using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>ADR-0052: title block properties on LargeWrite, read back and undone.</summary>
[Trait("Size", "Large")]
public sealed class ProjectUpdateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task TitleBlock_SetsTextAndLanguages_ReadsBack_OneUndoRestores()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string before = await ValuesAsync(session.Project);
        Dictionary<string, object?> args = new()
        {
            ["project"] = session.Project,
            ["properties"] = new object[]
            {
                new { id = 10015, value = "Fictional company" },
                new { id = 10011, value = "zh_CN@虚构项目;en_US@Fictional project;" },
            },
        };
        JsonElement preview = await eplan.CallAsync("eplan_project_update", args);
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        Assert.Equal(before, await ValuesAsync(session.Project));
        JsonElement set = await eplan.CallAsync("eplan_project_update", new(args) { ["dry_run"] = false });
        Assert.Equal("committed", set.GetProperty("execution").GetString());
        string after = await ValuesAsync(session.Project);
        Assert.Contains("Fictional company", after, StringComparison.Ordinal);
        Assert.Contains("虚构项目", after, StringComparison.Ordinal);
        await session.UndoAsync();
        Assert.Equal(before, await ValuesAsync(session.Project));
    }

    private async Task<string> ValuesAsync(string project)
    {
        JsonElement read = await eplan.CallAsync("eplan_project_get", new() { ["project"] = project, ["response_format"] = "detailed" });
        return string.Join("|", read.GetProperty("properties").EnumerateArray()
            .Where(p => p.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() is 10011 or 10015)
            .Select(p => p.GetProperty("value").ToString()));
    }
}
