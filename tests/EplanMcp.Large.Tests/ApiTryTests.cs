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
    private readonly TrialApprovals _approvals = new(ApiTryTools.Tool);
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
    private async Task<JsonElement> Locator(string project, string kind = "Page", string? page = null)
    {
        var args = new Dictionary<string, object?> { ["project"] = project, ["object_type"] = kind, ["limit"] = 1 };
        if (page is not null)
        { args["page"] = page; }
        JsonElement found = await eplan.CallAsync("eplan_object_find", args);
        return Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator");
    }
    private async Task<JsonElement> Call(string project, string member, object parameters, bool dry = false)
    {
        CallToolResult result = await _tool!.TryAsync(new Progress<ProgressNotificationValue>(), member.StartsWith("O:", StringComparison.Ordinal) ? member : "O:Eplan.EplApi.DataModel." + member,
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
        JsonElement result = await Call(project, "Page.Name", new { target = await Locator(project) });
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Equal(result.GetProperty("before").GetString(), result.GetProperty("after").GetString());
        Assert.Equal(0, result.GetProperty("totalChanges").GetInt32());
        ApprovalQuestion asked = Assert.Single(_approvals.Questions);
        Assert.Equal(ApiTryTools.Tool, asked.Tool);
        Assert.Contains(asked.Changes, c => c.StartsWith("read unverified API O:Eplan.EplApi.DataModel.Page.Name", StringComparison.Ordinal));
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }
    [Fact]
    public async Task ScratchCopy_PropertyWriteOneUndoRestoresValueAndEarlierHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string[] sentinel = await selection.CreatePagesAsync(1);
        JsonElement target = await Locator(selection.Project, page: sentinel[0]);
        string original = (await Call(selection.Project, "Page.Name", new { target })).GetProperty("value").GetString()!;
        string before = await selection.StateAsync();
        string renamed = selection.NextPage;
        JsonElement written = await Call(selection.Project, "Page.Name", new { target, value = renamed });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        JsonElement change = Assert.Single(written.GetProperty("changes").EnumerateArray(), c => c.GetProperty("key").GetString() == "property:O:Eplan.EplApi.DataModel.Page.Name");
        Assert.Equal(original, JsonDocument.Parse(change.GetProperty("before").GetString()!).RootElement.GetString());
        Assert.Equal(renamed, JsonDocument.Parse(change.GetProperty("after").GetString()!).RootElement.GetString());
        Assert.Equal(renamed, (await Call(selection.Project, "Page.Name", new { target = await Locator(selection.Project, page: renamed) })).GetProperty("value").GetString());
        Assert.Contains(_approvals.Questions, q => q.Changes.Any(c => c.StartsWith("write unverified API O:Eplan.EplApi.DataModel.Page.Name = ", StringComparison.Ordinal)));
        await selection.UndoAsync();
        Assert.Equal(original, (await Call(selection.Project, "Page.Name", new { target = await Locator(selection.Project, page: original) })).GetProperty("value").GetString());
        Assert.Equal(before, await selection.StateAsync());
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
    }

    [Fact]
    public async Task ScratchCopy_MethodEnumReadAndRecordWritePreserveSingleUndoHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string[] sentinel = await selection.CreatePagesAsync(1);
        JsonElement read = await Call(selection.Project, "Page.GetWriteProtectionFlagSet", new { target = await Locator(selection.Project, page: sentinel[0]), nKind = "Manual" });
        Assert.True(read.GetProperty("value").GetString() is "True" or "False");
        Assert.Equal(read.GetProperty("before").GetString(), read.GetProperty("after").GetString());
        string record = "ApiTry_" + Guid.NewGuid().ToString("N");
        JsonElement holder = await Locator(selection.Project, "PlaceHolder");
        string holderPage = holder.GetProperty("page").GetString()!;
        long oid = holder.GetProperty("oid").GetInt64();
        async Task<JsonElement> FreshHolder()
        {
            JsonElement found = await eplan.CallAsync("eplan_object_find", new Dictionary<string, object?> { ["project"] = selection.Project, ["object_type"] = "PlaceHolder", ["page"] = holderPage, ["limit"] = 200 });
            return Assert.Single(found.GetProperty("items").EnumerateArray(), item => item.GetProperty("locator").GetProperty("oid").GetInt64() == oid).GetProperty("locator");
        }
        Assert.Equal("-1", (await Call(selection.Project, "Graphics.PlaceHolder.FindRecord", new { target = await FreshHolder(), strName = record })).GetProperty("value").GetString());
        JsonElement written = await Call(selection.Project, "Graphics.PlaceHolder.AddRecord", new { target = await FreshHolder(), strRecordName = record });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Equal(written.GetProperty("value").GetString(), (await Call(selection.Project, "Graphics.PlaceHolder.FindRecord", new { target = await FreshHolder(), strName = record })).GetProperty("value").GetString());
        await selection.UndoAsync();
        Assert.Equal("-1", (await Call(selection.Project, "Graphics.PlaceHolder.FindRecord", new { target = await FreshHolder(), strName = record })).GetProperty("value").GetString());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
    }

    [Fact]
    public async Task ScratchCopy_TerminalLocatorReadsGeneratedPropertyWithoutChanges()
    {
        string project = await eplan.WriteProjectAsync();
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        JsonElement target = await Locator(project, "Terminal");
        JsonElement result = await Call(project, "EObjects.Terminal.IsMainTerminal", new { target });
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.True(result.GetProperty("value").GetString() is "True" or "False");
        Assert.Equal(result.GetProperty("before").GetString(), result.GetProperty("after").GetString());
        Assert.Equal(0, result.GetProperty("totalChanges").GetInt32());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScratchCopy_ServiceReadLeavesBusinessAndDesignFilesUnchanged()
    {
        string project = await eplan.WriteProjectAsync();
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(project);
        JsonElement result = await Call(project, "O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested",
            new { target = await Locator(project, "Project"), pFunction = await Locator(project, "Function") });
        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.True(result.GetProperty("value").GetString() is "True" or "False");
        Assert.Equal(result.GetProperty("before").GetString(), result.GetProperty("after").GetString());
        Assert.Equal(0, result.GetProperty("totalChanges").GetInt32());
        Assert.Contains(Assert.Single(_approvals.Questions).Changes, c => c.StartsWith("read unverified API O:Eplan.EplApi.HEServices.NameService.CanDeviceTagBeNested", StringComparison.Ordinal));
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(project).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScratchCopy_ServiceWriteOneUndoRestoresConnectionAndEarlierHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string sentinel = Assert.Single(await selection.CreatePagesAsync(1));
        JsonElement connection = await Locator(selection.Project, "Connection");
        long oid = connection.GetProperty("oid").GetInt64();
        EplanMcp.Contracts.SnapshotObject FindConnection(LiveSnapshots.Snapshot snapshot) => Assert.Single(snapshot.Pages.SelectMany(p => p.Objects), o => o.Kind == "connection" && o.Oid == oid);
        EplanMcp.Contracts.SnapshotObject original = FindConnection(await selection.SnapshotAsync());
        string before = await selection.StateAsync();
        JsonElement written = await Call(selection.Project, "O:Eplan.EplApi.HEServices.ConnectionService.SwapSourceAndTarget",
            new { target = await Locator(selection.Project, "Project"), connection });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Equal(JsonValueKind.Null, written.GetProperty("value").ValueKind);
        EplanMcp.Contracts.SnapshotObject swapped = FindConnection(await selection.SnapshotAsync());
        Assert.Equal(JsonSerializer.Serialize(original.Start), JsonSerializer.Serialize(swapped.End));
        Assert.Equal(JsonSerializer.Serialize(original.End), JsonSerializer.Serialize(swapped.Start));
        Assert.NotEqual(before, await selection.StateAsync());
        await selection.UndoAsync();
        Assert.Equal(before, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
    }
}
