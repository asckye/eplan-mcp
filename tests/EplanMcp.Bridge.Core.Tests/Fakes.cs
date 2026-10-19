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
