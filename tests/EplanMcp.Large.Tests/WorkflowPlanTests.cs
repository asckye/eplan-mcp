using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>P11 compilation on the checked write copy; no execute operation is sent.</summary>
[Trait("Size", "Large")]
public sealed class WorkflowPlanTests(LiveEplan eplan)
{
    [Fact]
    public async Task P11_WorkflowPlan_LargeWrite_CompileLeavesDesignFileHashesUnchanged()
    {
        string project = await eplan.WriteProjectAsync();
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new() { Project = project });
        const string prefix = "=GAA+A1&EFS1/";
        int last = snapshot.Chunks.SelectMany(c => c.Pages).Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(p => int.TryParse(p.Name[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty().Max();
        string page = prefix + (last + 1).ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(project);
        Assert.NotEmpty(before);
        JsonElement plan = await eplan.CallAsync("eplan_workflow_plan", new()
        {
            ["project"] = project,
            ["steps"] = new object[]
            {
                new { op = "page.create", args = new { pages = new[] { new { name = page, description = "Workflow compile check" } }, language = "en_US" } },
                new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page }, description = "Predicted update" } }, language = "en_US" } },
            },
        });
        Assert.Equal("compiled", plan.GetProperty("status").GetString());
        Assert.Matches("^[a-f0-9]{64}$", plan.GetProperty("planHash").GetString());
        Assert.Equal(2, plan.GetProperty("counts").GetProperty("steps").GetInt32());
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }
}
