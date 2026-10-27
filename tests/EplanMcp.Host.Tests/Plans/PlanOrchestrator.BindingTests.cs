using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Plans;

public sealed partial class PlanOrchestratorTests
{
    [Fact]
    public async Task BindingWrite_RunningPlan_TakesProjectLockBeforeBindingLock()
    {
        await Publish();
        _ready = await Prepare();
        BindingService bindings = Bindings();
        string folder = new PackLibrary(_locations, _config, _audit, _clock).Workspaces.FolderOf(_definition.Project.Path);
        Directory.CreateDirectory(folder);

        Task<ProjectBinding> writer = bindings.WriteAsync(Binding(), 0, "test", "bind", null, Token);

        Assert.False(writer.IsCompleted);
        using (var binding = new FileStream(Path.Combine(folder, ".binding.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(writer.IsCompleted);
        }
        _ready.Dispose();
        Assert.Equal(1, (await writer).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BindingWrite_WhilePlanWaitsForHuman_IsAllowedAndMakesApprovedPlanStale(bool noCabinets)
    {
        await Publish(ToolTier.Destructive);
        var channel = new DeferredChannel();
        _deferred = channel;
        Task<PlanPreparation> preparing = Prepare();
        ApprovalQuestion question = await channel.Seen.Task.WaitAsync(Token);

        ProjectBinding proposed = noCabinets ? Binding() with { Cabinets = [] } : Binding();
        ProjectBinding binding = await Bindings().WriteAsync(proposed, 0, "test", "bind", null, Token);
        channel.Answer.SetResult(new(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, ApprovalDecision.Granted));
        using PlanPreparation failed = await preparing;

        Assert.Equal(1, binding.Revision);
        await AssertFailure(failed, ErrorReason.PlanStale);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    private BindingService Bindings() => new(new PackLibrary(_locations, _config, _audit, _clock), _audit,
        new ApprovalService([_channel], _config, _clock, NullLogger<ApprovalService>.Instance), _bridge);

    private ProjectBinding Binding() => new()
    {
        BindingVersion = 2,
        Project = _definition.Project.Path,
        Revision = 0,
        ProjectValues = new(),
        PackRoots = [],
        Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = new JsonObject { ["all"] = true } }],
    };
}
