using System.Collections.Concurrent;

namespace EplanMcp.Coverage;

/// <summary>
/// Keeps what the generators read from the repository for one <c>check</c> or <c>generate</c>, which otherwise load and
/// compute the same catalog and tool model for each derived file. Outside a run nothing is kept, so a test that changes
/// a file between two calls sees the change. Callers must not change what they get.
/// </summary>
internal static class RunCache
{
    private static readonly AsyncLocal<ConcurrentDictionary<string, object>?> _current = new();

    public static IDisposable Begin()
    {
        _current.Value = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        return new Run();
    }

    public static T Get<T>(string key, Func<T> load) where T : notnull =>
        _current.Value is { } values ? (T)values.GetOrAdd(typeof(T).FullName + "|" + key, _ => load()) : load();

    private sealed class Run : IDisposable
    {
        public void Dispose() => _current.Value = null;
    }
}
