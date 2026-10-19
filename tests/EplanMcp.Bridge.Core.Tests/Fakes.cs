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

    public List<(string Path, string? Page, string? Category)> Reads { get; } = [];

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
