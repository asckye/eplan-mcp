using System.Collections.Concurrent;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>Runs work on the calling thread, catching everything as the real dispatcher must.</summary>
internal sealed class InlineDispatcher : IMainThreadDispatcher
{
    public int Calls { get; private set; }

    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
    {
        Calls++;
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

/// <summary>Refuses every piece of work with a fixed reason; the work must never run.</summary>
internal sealed class BusyDispatcher(BusyReason reason) : IMainThreadDispatcher
{
    public DispatchResult Run(Func<object?> work, TimeSpan startTimeout) => DispatchResult.Busy(reason);
}

internal sealed class ListLog : IBridgeLog
{
    public ConcurrentQueue<(BridgeLogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public void Write(BridgeLogLevel level, string message, Exception? exception = null) => Entries.Enqueue((level, message, exception));
}

internal sealed class FakeCatalog(params OpenProject[] projects) : IProjectCatalog
{
    public IReadOnlyList<OpenProject> ListOpenProjects() => projects;
}

/// <summary>Returns <see cref="Content"/> and records what the operation asked for.</summary>
internal sealed class FakeProjectReader : IProjectReader
{
    public ProjectContent Content { get; set; } = new();

    public List<(string Path, bool IncludeProperties, string? Language)> Calls { get; } = [];

    public ProjectContent Read(string projectPath, bool includeProperties, string? language)
    {
        Calls.Add((projectPath, includeProperties, language));
        return Content;
    }
}

/// <summary>
/// Pages of one project, filtered the way S6 found EPLAN filters (name prefix case-sensitive, page type exact), so
/// the operation's own filtering and paging run against realistic input. Records what it was asked.
/// </summary>
internal sealed class FakePageReader : IPageReader
{
    public List<PageSummary> Pages { get; } = [];

    public IReadOnlyCollection<string> PageTypes { get; set; } = ["Circuit", "TerminalDiagram", "TitlePage", "Overview"];

    public List<(string Path, string? NamePrefix, string? PageType, string? Language)> ListCalls { get; } = [];

    /// <summary>The functions placed on each page, by page name, for <see cref="ReadPage"/>.</summary>
    public Dictionary<string, List<FunctionRecord>> Functions { get; } = new(StringComparer.Ordinal);

    public List<PropertyEntry>? Properties { get; set; }

    public List<(string Path, string PageName, bool IncludeProperties, string? Language)> ReadCalls { get; } = [];

    public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language)
    {
        ListCalls.Add((projectPath, namePrefix, pageType, language));
        return [.. Pages.Where(p => (namePrefix is null || p.Name.StartsWith(namePrefix, StringComparison.Ordinal)) && (pageType is null || p.Type == pageType))];
    }

    public IReadOnlyList<string> PageNames(string projectPath) => [.. Pages.Select(p => p.Name)];

    /// <summary>Exact and case-sensitive, like EPLAN's exact name filter.</summary>
    public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language)
    {
        ReadCalls.Add((projectPath, pageName, includeProperties, language));
        PageSummary? page = Pages.FirstOrDefault(p => p.Name == pageName);
        return page is null
            ? null
            : new PageContent
            {
                Page = new PageDetail { Name = page.Name, Type = page.Type, TypeName = page.Type + " (UI)", Description = page.Description },
                Functions = Functions.TryGetValue(page.Name, out List<FunctionRecord>? functions) ? functions : [],
                Properties = includeProperties ? Properties : null,
            };
    }
}

/// <summary>
/// A project's functions as an EPLAN adapter would read them: page and category narrow the list like FunctionsFilter,
/// everything else is left to the operation. Records every read.
/// </summary>
internal sealed class FakeDeviceReader : IDeviceReader
{
    private static readonly string[] _categories = ["Coil", "NoContact", "NcContact", "Motor", "CircuitBreaker", "Terminal", "PLCBox", "Cable"];

    public List<FunctionRecord> Functions { get; } = [];

    /// <summary>What <see cref="ReadDevice"/> returns, by the main function's full tag (ordinal).</summary>
    public Dictionary<string, DeviceRecord> Devices { get; } = new(StringComparer.Ordinal);

    public List<(string Path, string? Page, string? Category)> Reads { get; } = [];

    public List<(string Path, string Tag, string? Language, bool WithProperties)> DeviceReads { get; } = [];

    public int NameReads { get; private set; }

    public IReadOnlyList<FunctionName> ReadMainFunctionNames(string projectPath)
    {
        NameReads++;
        return [.. Functions.Where(f => f.IsMain).Select(f => new FunctionName { Name = f.Name, VisibleName = f.VisibleName })];
    }

    public DeviceRecord? ReadDevice(string projectPath, string tag, string? language, bool withProperties)
    {
        DeviceReads.Add((projectPath, tag, language, withProperties));
        return language == "xx_XX"
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "'language' is not a language EPLAN knows.")
            : Devices.GetValueOrDefault(tag);
    }

    public bool IsCategory(string category) => _categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FunctionRecord>? ReadFunctions(string projectPath, string? page, string? category)
    {
        Reads.Add((projectPath, page, category));
        if (page is not null && !Functions.Any(f => f.Page == page))
        {
            return null;
        }

        return [.. Functions.Where(f => (page is null || f.Page == page) && (category is null || string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)))];
    }
}

/// <summary>
/// A project's connections narrowed the way the EPLAN adapter narrows them: by device, those starting or ending at the
/// device's main function or one of its auxiliary functions (here: the same tag, or the tag with a ':' suffix, S6); by
/// page, those placed on it. Records every read.
/// </summary>
internal sealed class FakeConnectionReader : IConnectionReader
{
    public List<string> Pages { get; } = [];

    public List<ConnectionRecord> Connections { get; } = [];

    public List<(string Path, string? Device, string? Page)> Reads { get; } = [];

    public IReadOnlyList<ConnectionRecord>? ReadConnections(string projectPath, string? device, string? page)
    {
        Reads.Add((projectPath, device, page));
        if (page is not null && !Pages.Contains(page, StringComparer.Ordinal))
        {
            return null;
        }

        return [.. Connections.Where(c => (device is null || At(c.StartDevice, device) || At(c.EndDevice, device)) && (page is null || c.Page == page))];
    }

    private static bool At(string? function, string device) =>
        function is not null && (function == device || function.StartsWith(device + ":", StringComparison.Ordinal));
}

/// <summary>A project's part references and part data as the EPLAN adapter reads them; "xx_XX" is a language EPLAN does not know.</summary>
internal sealed class FakePartReader : IPartReader
{
    public ProjectParts Parts { get; } = new();

    public List<(string Path, string? Language)> Reads { get; } = [];

    public ProjectParts ReadParts(string projectPath, string? language)
    {
        Reads.Add((projectPath, language));
        return language == "xx_XX"
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "'language' is not a language EPLAN knows.")
            : Parts;
    }
}

/// <summary>An operation whose behaviour is a delegate, for router and connection tests.</summary>
internal sealed class DelegateOperation(string name, Func<object?> execute) : IOperation
{
    public string Name => name;

    public object ParseArgs(JToken? args) => args ?? JValue.CreateNull();

    public object? Execute(object args) => execute();
}

internal sealed class FixedMapper(BridgeError? error) : IExceptionMapper
{
    public BridgeError? Map(Exception exception) => error;
}

/// <summary>
/// Transactions that behave like EPLAN's SafetyPoint as far as a fake project can: <see cref="Snapshot"/> runs when one
/// begins and returns how to restore that state, which a rollback does. Records every call in order and throws where
/// <see cref="FailAt"/> says ("Begin", "Commit", "CloseUndo", "Rollback" or "Dispose").
/// </summary>
internal sealed class FakeTransactions : ITransactionScopeFactory
{
    public List<string> Calls { get; } = [];

    public string? FailAt { get; set; }

    public Func<Action>? Snapshot { get; set; }

    public ITransactionScope Begin(string undoDescription)
    {
        Step("Begin " + undoDescription, "Begin");
        return new Scope(this, Snapshot?.Invoke());
    }

    private void Step(string call, string step)
    {
        Calls.Add(call);
        if (FailAt == step)
        {
            throw new InvalidOperationException(step + " failed");
        }
    }

    private sealed class Scope(FakeTransactions owner, Action? restore) : ITransactionScope
    {
        public void Commit() => owner.Step("Commit", "Commit");

        public void CloseUndo() => owner.Step("CloseUndo", "CloseUndo");

        public void Rollback()
        {
            owner.Step("Rollback", "Rollback");
            restore?.Invoke();
        }

        public void Dispose() => owner.Step("Dispose", "Dispose");
    }
}
