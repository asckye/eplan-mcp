using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

public abstract class PlanProtocolFixture : IDisposable
{
    private protected PlanOccupiedName[] _additionalNames = [];
    private protected readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-p23-").FullName);
    private protected readonly ManualTime _clock = TaskTestData.Clock();
    private protected readonly FakeBridgeClient _fake = new();
    private protected readonly ControlledBridge _bridge;
    private protected PlanSnapshot _current;
    private protected McpServer? _server;
    private protected RequestId _requestId;
    private protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected PlanProtocolFixture()
    {
        _current = PlanVerificationTestData.Copy(PlanScopeTestData.Baseline([], ["+A1/1"], Path.Combine(_locations.DefaultScratch, "Line1")).Snapshot, _ => { });
        _bridge = new(_fake);
        _fake.Projects.Add(new() { Name = _current.Project.Name, Path = _current.Project.Path });
        _fake.Status = new([new("fake-p23", Environment.ProcessId)], new()
        {
            ProtocolVersion = BridgeProtocol.CurrentVersion,
            Eplan = new() { ProcessId = Environment.ProcessId, Version = "2026", Variant = "Pro Panel" },
            Operations = [OperationNames.PlanStepExecute, OperationNames.OutcomeGet, OperationNames.ProjectList, OperationNames.PlanNames, OperationNames.ProjectSnapshot, OperationNames.PlanFunctions,
                OperationNames.DeviceList, OperationNames.ProjectBackup, OperationNames.PageCreatePlan, OperationNames.PageUpdatePlan,
                OperationNames.PageCreateExecute, OperationNames.PageUpdateExecute],
        }, null);
        _fake.Handlers[OperationNames.ProjectSnapshot] = a => new ProjectSnapshotData
        {
            Project = _current.Project.Name,
            ProjectPath = _current.Project.Path,
            ProjectGuid = _current.Project.Guid,
            Profile = _current.Profile,
            Format = _current.Format,
            Pages = [.. _current.Pages.Where(p => ((ProjectSnapshotArgs)a!).Pages is not { } pages || pages.Contains(p.Name, StringComparer.Ordinal))],
            PageCount = ((ProjectSnapshotArgs)a!).Pages?.Count ?? _current.Pages.Length,
            SnapshotHash = ((ProjectSnapshotArgs)a!).Pages is null ? _current.ComposedHash : null,
            Complete = true,
        };
        _fake.Handlers[OperationNames.PlanNames] = _ => Names();
        _fake.Handlers[OperationNames.DeviceList] = _ => new ListData<DeviceListItem> { Project = "Line1" };
        _fake.Handlers[OperationNames.PlanFunctions] = _ => new PlanFunctionsData();
        _fake.Handlers[OperationNames.PageCreatePlan] = _ => Fresh("page.create", "+A1/3", "Power");
        _fake.Handlers[OperationNames.PageUpdatePlan] = a => Fresh("page.update", Assert.IsType<PageUpdateArgs>(a).Pages[0].Name, Assert.IsType<PageUpdateArgs>(a).Pages[0].Description);
        _fake.Handlers[OperationNames.ProjectBackup] = a =>
        {
            string path = Assert.IsType<ProjectBackupArgs>(a).Target!.Path;
            File.WriteAllText(path, "initial backup");
            return new ProjectBackupData { Path = path, Pages = _current.Pages.Length, Seconds = 0.1m };
        };
        _fake.Handlers[OperationNames.PlanStepExecute] = a =>
        {
            var step = Assert.IsType<PlanStepExecuteArgs>(a);
            _current = step.PageCreate is not null ? PlanVerificationTestData.After("page.create", _current)
                : PlanVerificationTestData.Copy(_current, pages => PlanVerificationTestData.Description(pages.Single(p => p.Name == step.PageUpdate!.Pages[0].Name).Objects[0], step.PageUpdate!.Pages[0].Description));
            return JsonSerializer.SerializeToElement(new { });
        };
    }

    private protected static async Task<string> Compile(McpClient client)
    {
        var args = new Dictionary<string, object?>
        {
            ["project"] = "Line1",
            ["steps"] = new object[]
            {
                new { op = "page.create", args = new { pages = new[] { new { name = "+A1/3", description = "Power" } }, language = "en_US" } },
                new { op = "page.update", args = new { pages = new[] { new { name = new { from_step = 1, page = "+A1/3" }, description = "New" } }, language = "en_US" } },
            },
        };
        return ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, args)).GetProperty("planId").GetString()!;
    }
    private protected static Task<CallToolResult> Call(McpClient client, string name, Dictionary<string, object?> args) => client.CallToolAsync(name, args, cancellationToken: Token).AsTask();

    private protected PlanNamesData Names()
    {
        PlanOccupiedName[] names = [.. _current.Pages.SelectMany(p => p.Objects).Where(o => !string.IsNullOrEmpty(o.Name))
            .Select(o => new PlanOccupiedName(o.Kind, o.Name!)).Concat(_additionalNames).Distinct()];
        return new()
        {
            Project = _current.Project.Name,
            ProjectPath = _current.Project.Path,
            ProjectGuid = _current.Project.Guid,
            Complete = true,
            Names = [.. names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
            NamesHash = PlanSnapshotHashes.Names(_current.Project.Path, names),
        };
    }

    private protected WritePlan Fresh(string op, string name, string? description)
    {
        SnapshotObject? page = _current.Pages.FirstOrDefault(p => p.Name == name)?.Objects[0];
        return new()
        {
            Project = "Line1",
            ProjectPath = _current.Project.Path,
            Fingerprint = "temporary",
            Changes = [new() { Action = op == "page.create" ? "create" : "update", Kind = "page", Name = name,
                Property = op == "page.update" ? "description (en_US)" : null,
                OldValue = page?.Values.SingleOrDefault(v => v.Id == 11011)?.Texts?.SingleOrDefault(t => t.Language == "en_US")?.Text,
                NewValue = op == "page.update" ? description : null }],
        };
    }

    private protected async Task AdvanceUntil(Task task, TimeSpan tick)
    {
        for (int i = 0; !task.IsCompleted && i < 100; i++)
        {
            await Task.Delay(10, Token);
            _clock.Advance(tick);
        }
        Assert.True(task.IsCompleted, "The wait did not end with the controlled clock.");
    }

    private protected async Task WithClient(Func<McpClient, IServiceProvider, Task> check, Action<IServiceCollection>? configure = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_locations);
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddEplanMcp().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => (context, cancellationToken) =>
            {
                _server = context.Server;
                _requestId = context.JsonRpcRequest.Id;
                return next(context, cancellationToken);
            }));
        builder.Services.AddSingleton<IBridgeClient>(_bridge);
        configure?.Invoke(builder.Services);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: Token);
            await check(client, host.Services);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_locations.Root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private protected sealed class ControlledBridge(FakeBridgeClient inner) : IBridgeClient
    {
        public TaskCompletionSource? BackupGate { get; set; }
        public TaskCompletionSource BackupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? StepGate { get; set; }
        public TaskCompletionSource StepEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken WorkerToken { get; private set; }
        public Task<BridgeStatus> GetStatusAsync(bool refresh, CancellationToken cancellationToken) => inner.GetStatusAsync(refresh, cancellationToken);
        public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            WorkerToken = cancellationToken;
            return inner.GetStatusAsync(cancellationToken);
        }
        public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => inner.SelectAsync(processId, cancellationToken);
        public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken) => inner.InvokeAsync<TArgs, TData>(operation, args, cancellationToken);
        public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => inner.FetchOutcomeAsync(processId, requestId, cancellationToken);
        public async Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs args, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            if (operation == OperationNames.PlanStepExecute && StepGate is not null)
            {
                StepEntered.TrySetResult();
                await StepGate.Task.WaitAsync(cancellationToken);
            }
            return await inner.InvokeWriteAsync<TArgs, TData>(operation, args, requestId, limit, progress, cancellationToken);
        }
        public async Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
        {
            if (operation == OperationNames.ProjectBackup && BackupGate is not null)
            {
                BackupEntered.TrySetResult();
                await BackupGate.Task.WaitAsync(cancellationToken);
            }
            return await inner.InvokeAsync<TArgs, TData>(operation, args, timeout, progress, cancellationToken);
        }
    }
}
