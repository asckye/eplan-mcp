using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Packs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class WorkflowCabinetTests
{
    private static readonly string[] _evidenceWindows = ["1:before", "1:after"];

    [Theory]
    [InlineData("binding")]
    [InlineData("mapping")]
    [InlineData("config")]
    [InlineData("config_file")]
    [InlineData("rule_version")]
    [InlineData("rule_file")]
    [InlineData("knowledge_version")]
    [InlineData("objects")]
    [InlineData("out_of_scope")]
    public async Task Compiler3_ApprovedEvidenceChanges_StaleWithoutBackupOrExecution(string change)
    {
        await Seed("pass");
        var channel = new FakeApprovalChannel("engineer", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                ProjectBinding binding = PackJson.ReadBinding(File.ReadAllBytes(BindingFile));
                if (change == "binding")
                { SaveBinding(binding with { Revision = 8 }); }
                if (change == "mapping")
                {
                    binding.Cabinets[0].Structure!["prefixes"]![0]!["location"] = "+A2";
                    SaveBinding(binding);
                }
                if (change == "config")
                {
                    binding.Cabinets[0].Values!["mode"]!["default"] = "star_delta";
                    SaveBinding(binding);
                }
                if (change == "rule_version")
                { SaveBinding(NextVersion(binding)); }
                if (change is "config_file" or "rule_file")
                { File.AppendAllText(Path.Combine(Folder, change == "config_file" ? "knowledge/config_layer/company.json" : "knowledge/rule/motor.json"), "\n"); }
                if (change == "knowledge_version")
                { File.AppendAllText(Path.Combine(Folder, "knowledge/workflow/create.json"), "\n"); }
                if (change is "objects" or "out_of_scope")
                {
                    _bridge.Handlers[OperationNames.ProjectSnapshot] = args => ChangedSnapshot((ProjectSnapshotArgs)args!, change == "out_of_scope");
                }
            },
        };
        ApplyBridge();
        await WithClient(async (client, store) =>
        {
            PlanRecord plan = await Record(client, store, Update());
            JsonElement data = (await client.CallToolAsync("eplan_plan_apply", new Dictionary<string, object?> { ["plan_id"] = plan.Definition.PlanId }, cancellationToken: Token)).StructuredContent!.Value;
            Assert.Equal("not_started", data.GetProperty("execution").GetString());
            Assert.Equal(ErrorReason.PlanStale, data.GetProperty("error").GetProperty("reason").GetString());
            Assert.Contains("new plan", data.GetProperty("error").GetProperty("message").GetString()!, StringComparison.Ordinal);
            Assert.Equal(PlanExecution.NotStarted, (await store.ReadAsync(plan.Definition.PlanId, Token))!.State.Result!.Execution);
        }, services => UseChannel(services, channel));
        Assert.Single(channel.Questions);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task Compiler3_LocalRuleReviewChangesAfterApproval_StaleBeforeBackup()
    {
        await Seed("pass");
        ApplyBridge();
        var channel = new ChangedReviewChannel(async () =>
        {
            var promotion = (await Audit.ReadAsync(Token)).Single(r => (string?)r["kind"] == "knowledge_review");
            await Audit.AppendAsync(new JsonObject
            {
                ["kind"] = "knowledge_review",
                ["resource"] = promotion["resource"]!.DeepClone(),
                ["decision"] = "reject",
            }, Token);
        });
        await WithClient(async (client, store) =>
        {
            PlanRecord plan = await Record(client, store, Update());
            JsonElement data = (await client.CallToolAsync("eplan_plan_apply", new Dictionary<string, object?> { ["plan_id"] = plan.Definition.PlanId }, cancellationToken: Token)).StructuredContent!.Value;
            Assert.Equal("not_started", data.GetProperty("execution").GetString());
            Assert.Equal(ErrorReason.PlanStale, data.GetProperty("error").GetProperty("reason").GetString());
        }, services => UseChannel(services, channel));
        Assert.True(channel.Asked);
        Assert.Empty(_bridge.Writes);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task Compiler3_ObjectMovesOutsideCabinetAfterBackup_StopsBeforeWriteAndSavesLocalEvidence()
    {
        await Seed();
        ApplyBridge();
        Func<object?, object> backup = _bridge.Handlers[OperationNames.ProjectBackup];
        _bridge.Handlers[OperationNames.ProjectBackup] = args =>
        {
            object result = backup(args);
            _bridge.Handlers[OperationNames.ProjectSnapshot] = request => ChangedSnapshot((ProjectSnapshotArgs)request!, true);
            return result;
        };
        await WithClient(async (client, store) =>
        {
            PlanRecord plan = await Record(client, store, Update());
            JsonElement data = (await client.CallToolAsync("eplan_plan_apply", new Dictionary<string, object?> { ["plan_id"] = plan.Definition.PlanId }, cancellationToken: Token)).StructuredContent!.Value;
            Assert.Equal("not_started", data.GetProperty("execution").GetString());
            Assert.Equal(ErrorReason.PlanStale, data.GetProperty("error").GetProperty("reason").GetString());
            PlanResult result = (await store.ReadAsync(plan.Definition.PlanId, Token))!.State.Result!;
            string reference = result.Coverage!.Value.GetProperty("evidence").GetProperty("1:before").GetString()!;
            using JsonDocument evidence = JsonDocument.Parse(await store.ReadEvidenceAsync(plan.Definition.PlanId, reference, Token));
            Assert.False(evidence.RootElement.GetProperty("wholeProject").GetBoolean());
            Assert.Equal("+A1/1", Assert.Single(evidence.RootElement.GetProperty("snapshot").GetProperty("pages").EnumerateArray()).GetProperty("name").GetString());
        }, services => UseChannel(services, new FakeApprovalChannel("engineer", ApprovalDecision.Granted)));
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
        Assert.Empty(_bridge.Writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Compiler2And3_Create_BackupOutcomeAndLocalEvidenceUseExistingPath(bool bound, bool recovered)
    {
        if (bound)
        { await Seed(); }
        ApplyBridge();
        bool created = false;
        Func<object?, object> names = _bridge.Handlers[OperationNames.PlanNames];
        _bridge.Handlers[OperationNames.PlanNames] = args =>
        {
            var data = (PlanNamesData)names(args);
            if (created)
            { data.Names.Add(new() { Kind = "page", Name = "+A1/3" }); }
            data.NamesHash = PlanSnapshotHashes.Names(ContextTestSnapshot.ProjectPath, data.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name)));
            return data;
        };
        _bridge.Handlers[OperationNames.ProjectSnapshot] = args =>
        {
            var request = (ProjectSnapshotArgs)args!;
            Assert.NotNull(request.Pages);
            ProjectSnapshotData data = ContextTestSnapshot.Data(request);
            if (created && request.Pages.Contains("+A1/3", StringComparer.Ordinal))
            {
                var page = new SnapshotObject { Kind = "page", Type = 4, Oid = 99, Name = "+A1/3", PageType = "Circuit" };
                page.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(page));
                data.Pages.Add(new() { Name = page.Name, Type = page.Type, Oid = page.Oid, Objects = [page], Hash = PlanSnapshotHashes.Page([page]) });
                data.MissingPages.Remove("+A1/3");
                data.PageCount++;
            }
            return data;
        };
        _bridge.WriteRecovered = recovered;
        _bridge.DuringWrite = _ => created = true;
        _bridge.Handlers[OperationNames.PlanStepExecute] = _ => JsonSerializer.SerializeToElement(new { });
        await WithClient(async (client, store) =>
        {
            var request = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["cabinet"] = bound ? "a1" : null,
                ["steps"] = new[] { new { op = "page.create", args = new { pages = new[] { new { name = "+A1/3" } } } } }
            };
            PlanRecord plan = await Record(client, store, request);
            Assert.Equal(bound ? "3" : "2", plan.Definition.CompilerVersion);
            JsonElement data = ContextToolTests.Data(await client.CallToolAsync("eplan_plan_apply", new Dictionary<string, object?> { ["plan_id"] = plan.Definition.PlanId }, cancellationToken: Token));
            Assert.Equal("committed", data.GetProperty("execution").GetString());
            PlanResult result = (await store.ReadAsync(plan.Definition.PlanId, Token))!.State.Result!;
            Assert.Equal("passed", result.Verification);
            Assert.Equal(recovered, Assert.Single(result.Steps).Recovered);
            Assert.True(Assert.Single(result.Backups).Usable);
            foreach (string key in _evidenceWindows)
            {
                byte[] bytes = await store.ReadEvidenceAsync(plan.Definition.PlanId, result.Coverage!.Value.GetProperty("evidence").GetProperty(key).GetString()!, Token);
                using JsonDocument evidence = JsonDocument.Parse(bytes);
                Assert.False(evidence.RootElement.GetProperty("wholeProject").GetBoolean());
                Assert.All(evidence.RootElement.GetProperty("snapshot").GetProperty("pages").EnumerateArray(), p => Assert.Equal("+A1/3", p.GetProperty("name").GetString()));
            }
        }, services => UseChannel(services, new FakeApprovalChannel("engineer", ApprovalDecision.Granted)));
        Assert.Single(_bridge.Writes);
        Assert.Single(_bridge.Calls, c => c.Operation == OperationNames.ProjectBackup);
    }

    private void ApplyBridge()
    {
        _bridge.Status.Handshake!.Eplan.ProcessId = Environment.ProcessId;
        _bridge.Status.Handshake.Operations = [.. _bridge.Status.Handshake.Operations!, OperationNames.PlanStepExecute, OperationNames.OutcomeGet];
        _bridge.Status = _bridge.Status with { Instances = [new("fake-only", Environment.ProcessId)] };
        _bridge.Handlers[OperationNames.ProjectBackup] = args =>
        {
            var request = (ProjectBackupArgs)args!;
            File.WriteAllText(request.Target!.Path, "complete backup");
            return new ProjectBackupData { Path = request.Target.Path, Pages = 2 };
        };
    }

    private ProjectBinding NextVersion(ProjectBinding binding)
    {
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(Folder, PackManifest.FileName))) with { Version = "2.0.0" };
        string next = Packs.Installed.FolderOf(manifest.Id, manifest.Version);
        foreach (PackResource resource in manifest.Resources)
        {
            string destination = Path.Combine(next, resource.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(Folder, resource.Path), destination);
        }
        byte[] bytes = PackJson.Serialize(manifest);
        var pin = new PackReference { Id = manifest.Id, Version = manifest.Version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(next, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(next, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, [])));
        return binding with
        {
            Locks = [pin],
            PackRoots = [new() { Id = pin.Id, Version = pin.Version }],
            Cabinets = [.. binding.Cabinets.Select(c => c with { Layers = [.. c.Layers!.Select(l => l.Replace("@1.0.0:", "@2.0.0:", StringComparison.Ordinal))] })],
        };
    }

    private static void UseChannel(IServiceCollection services, IApprovalChannel channel)
    {
        services.RemoveAll<IApprovalChannel>();
        services.AddSingleton<IApprovalChannel>(channel);
    }

    private sealed class ChangedReviewChannel(Func<Task> change) : IApprovalChannel
    {
        public string Name => "engineer";
        public string Where => "in the test";
        public bool Asked { get; private set; }
        public async Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
        {
            Asked = true;
            await change();
            return new(question.RequestId, question.Plan.Fingerprint, question.ArgumentsHash, ApprovalDecision.Granted);
        }
    }

    private static ProjectSnapshotData ChangedSnapshot(ProjectSnapshotArgs request, bool outside)
    {
        ProjectSnapshotData data = ContextTestSnapshot.Data(request);
        SnapshotPage page = data.Pages.Single(p => p.Name == "+A1/1");
        SnapshotObject function = page.Objects.Single(o => o.Kind == "function");
        if (outside)
        { function.Name = "+A10-K1"; }
        else
        { function.Oid = 999; }
        function.Hash = SnapshotCanonical.Sha256(SnapshotCanonical.Text(function));
        page.Hash = PlanSnapshotHashes.Page(page.Objects);
        return data;
    }
}
