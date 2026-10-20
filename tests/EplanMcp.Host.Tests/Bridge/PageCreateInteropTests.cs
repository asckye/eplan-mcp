using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// eplan_page_create end to end without EPLAN: the tool, the write pipeline with its audit, a real BridgeClient and
/// named pipe, and Bridge.Core's page.create operations over a fake project whose transactions restore it on rollback.
/// Quality scenarios Q2 (a failure inside the transaction leaves the project as it was) and Q6 (a change after the
/// plan is PLAN_STALE) as far as they do not need EPLAN.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageCreateInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-page-interop-").FullName);
    private readonly Project _project = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;
    private readonly WritePipeline _pipeline;

    public PageCreateInteropTests()
    {
        _project.Open = new OpenProject { Name = "Line1", Path = Path.Combine(_locations.DefaultScratch, "Line1") };
        _project.Pages.AddRange(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"]);
        IOperation[] operations =
        [
            new PageCreatePlanOperation(_project, _project, _project),
            new PageCreateExecuteOperation(_project, _project, _project, _project),
        ];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        _pipeline = new WritePipeline(
            _client,
            HostConfig.Defaults(_locations),
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(_client, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([], HostConfig.Defaults(_locations), TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task Pages_AreCreatedInOneCommittedTransactionAndAudited()
    {
        JsonElement data = Structured(await Create("=GAA+A1&EFS1/5", "=GAA+A1&EFS1/6"));

        Assert.Equal(("committed", "AI: eplan_page_create"), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString()));
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/5", "=GAA+A1&EFS1/6"], _project.Pages);
        Assert.Equal(["Begin AI: eplan_page_create", "Commit", "CloseUndo", "Dispose"], _project.Calls);
        Assert.Equal(["execution_started", "execution_result"], AuditKinds());
    }

    [Fact]
    public async Task Q2_AFailureOnTheSecondPage_LeavesTheProjectAsItWas()
    {
        _project.FailOnCreate = 2;

        JsonElement error = Error(await Create("=GAA+A1&EFS1/5", "=GAA+A1&EFS1/6"));

        Assert.Equal(("INTERNAL", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2"], _project.Pages);
        Assert.DoesNotContain("Commit", _project.Calls);
        Assert.Equal("rolled_back", AuditRecords()[^1]["execution"]!.GetValue<string>());
        Assert.Empty(SuspensionStore.For(_locations).All());
    }

    [Fact]
    public async Task Q6_APageAddedThereAfterThePlan_IsPlanStaleAndNothingIsCreated()
    {
        // The plan reads the page names once; the engineer adds a page before the execution reads them again.
        _project.BeforeSecondRead = () => _project.Pages.Add("=GAA+A1&EFS1/3");

        JsonElement error = Error(await Create("=GAA+A1&EFS1/5"));

        Assert.Equal(("PLAN_STALE", "not_started", false), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("retryable").GetBoolean()));
        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/3"], _project.Pages);
        Assert.Empty(_project.Calls);
    }

    [Fact]
    public async Task ANameInUse_IsAlreadyExistsAtThePlanAndNeverExecuted()
    {
        JsonElement error = Error(await Create("=gaa+a1&efs1/2"));

        Assert.Equal(("ALREADY_EXISTS", "not_started", "=GAA+A1&EFS1/2"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("existing").GetString()));
        Assert.False(Directory.Exists(_locations.Audit));
    }

    private Task<CallToolResult> Create(params string[] names) => PageWriteTools.PageCreateAsync(
        _pipeline,
        "Line1",
        [.. names.Select(n => new PageWriteTools.NewPage { Name = n })],
        null,
        false,
        cancellationToken: TestContext.Current.CancellationToken);

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }

    private List<JsonObject> AuditRecords() =>
        [.. Directory.EnumerateFiles(_locations.Audit, AuditLog.FilePattern).SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!.AsObject())];

    private string[] AuditKinds() => [.. AuditRecords().Select(r => r["kind"]!.GetValue<string>())];

    /// <summary>One open project as the adapters would see it, with transactions that restore it on rollback.</summary>
    private sealed class Project : IProjectCatalog, IPageReader, IPageWriter, ITransactionScopeFactory
    {
        private int _reads;
        private int _creates;

        public OpenProject Open { get; set; } = new();

        public List<string> Pages { get; } = [];

        public List<string> Calls { get; } = [];

        public int? FailOnCreate { get; set; }

        public Action? BeforeSecondRead { get; set; }

        public IReadOnlyCollection<string> PageTypes => ["Circuit"];

        public IReadOnlyList<OpenProject> ListOpenProjects() => [Open];

        public IReadOnlyList<string> PageNames(string projectPath)
        {
            if (++_reads == 2)
            {
                BeforeSecondRead?.Invoke();
            }

            return [.. Pages];
        }

        public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language) => [];

        public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language) => null;

        public bool KnowsLanguage(string language) => true;

        public PageSummary Create(string projectPath, PageName name, string pageType, string? description, string? language)
        {
            if (++_creates == FailOnCreate)
            {
                throw new InvalidOperationException("EPLAN failed");
            }

            Pages.Add(name.Text);
            return new PageSummary { Name = name.Text, Type = pageType, Description = description };
        }

        public ITransactionScope Begin(string undoDescription)
        {
            Calls.Add("Begin " + undoDescription);
            return new Scope(this, [.. Pages]);
        }

        private sealed class Scope(Project project, string[] before) : ITransactionScope
        {
            public void Commit() => project.Calls.Add("Commit");

            public void CloseUndo() => project.Calls.Add("CloseUndo");

            public void Rollback()
            {
                project.Calls.Add("Rollback");
                project.Pages.Clear();
                project.Pages.AddRange(before);
            }

            public void Dispose() => project.Calls.Add("Dispose");
        }
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
