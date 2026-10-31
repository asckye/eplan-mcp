using System.Text.Json;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>Prepared LargeWrite scratch copy only; every trial approval is answered by the engineer.</summary>
[Trait("Size", "Large")]
public sealed class ApiTryTests(LiveEplan eplan) : IAsyncLifetime
{
    private readonly string _home = Directory.CreateTempSubdirectory("eplanmcp-large-api-try-").FullName;
    private ServiceProvider? _services;
    private ApiTryTools? _tool;
    private readonly TrialApprovals _approvals = new();
    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HostDataLocations(_home));
        services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ApiTry = ActionRunSwitch.On });
        services.AddEplanMcp();
        services.RemoveAll<IApprovalChannel>();
        services.AddSingleton<IApprovalChannel>(_approvals);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<IBridgeClient>().SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
        _tool = ActivatorUtilities.CreateInstance<ApiTryTools>(_services);
    }
    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        { await _services.DisposeAsync(); }
        Directory.Delete(_home, recursive: true);
    }
    private async Task<JsonElement> Locator(string project)
    {
        JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = "Page", ["limit"] = 1 });
        return Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator");
    }
    private async Task<JsonElement> Call(string project, string member, object parameters, bool dry = false)
    {
        CallToolResult result = await _tool!.TryAsync(new Progress<ProgressNotificationValue>(), "O:Eplan.EplApi.DataModel.Page." + member,
            project, JsonSerializer.SerializeToElement(parameters), dry, TestContext.Current.CancellationToken);
        Assert.False(result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)));
        return result.StructuredContent!.Value;
    }
    [Fact]
    public async Task ScratchCopy_ReadGroupLeavesBusinessAndDesignFingerprintsUnchanged()
    {
        string project = await eplan.WriteProjectAsync();
        Assert.True(ScratchPaths.IsInside(project, [LiveEplan.ScratchRoot()]));
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        JsonElement result = await Call(project, "Name", new { target = await Locator(project) });
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Equal(result.GetProperty("before").GetString(), result.GetProperty("after").GetString());
        Assert.Equal(0, result.GetProperty("totalChanges").GetInt32());
        ApprovalQuestion asked = Assert.Single(_approvals.Questions);
        Assert.Equal(ApiTryTools.Tool, asked.Tool);
        Assert.Contains(asked.Changes, c => c.Contains("unverified API O:Eplan.EplApi.DataModel.Page.Name", StringComparison.Ordinal));
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }
    [Fact(Skip = "Read-only first slice: enable only after generated property-write dispatch and transaction verification are added.")]
    public async Task ScratchCopy_PropertyWriteOneUndoRestoresValueAndEarlierHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string[] sentinel = await selection.CreatePagesAsync(1);
        string original = (await Call(selection.Project, "GridSize", new { target = await Locator(selection.Project) })).GetProperty("value").GetString()!;
        string before = await selection.StateAsync();
        double grid = double.Parse(original, System.Globalization.CultureInfo.InvariantCulture);
        JsonElement written = await Call(selection.Project, "GridSize", new { target = await Locator(selection.Project), value = grid + 0.5 });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.NotEqual(original, (await Call(selection.Project, "GridSize", new { target = await Locator(selection.Project) })).GetProperty("value").GetString());
        await selection.UndoAsync();
        Assert.Equal(original, (await Call(selection.Project, "GridSize", new { target = await Locator(selection.Project) })).GetProperty("value").GetString());
        Assert.Equal(before, await selection.StateAsync());
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
    }
}

/// <summary>The engineer of these trials: grants each eplan_api_try question bound to exactly what was asked, and keeps every question.</summary>
internal sealed class TrialApprovals : IApprovalChannel
{
    private readonly List<ApprovalQuestion> _questions = [];
    public string Name => ScriptedApprovalChannel.ChannelName;
    public string Where => "in the Large test's script";
    public IReadOnlyList<ApprovalQuestion> Questions { get { lock (_questions) { return [.. _questions]; } } }
    public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        lock (_questions)
        { _questions.Add(question); }
        ApprovalDecision decision = question.Tool == ApiTryTools.Tool && question.Release is null ? ApprovalDecision.Granted : ApprovalDecision.Denied;
        return Task.FromResult<ApprovalAnswer?>(new ApprovalAnswer(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, decision));
    }
}
