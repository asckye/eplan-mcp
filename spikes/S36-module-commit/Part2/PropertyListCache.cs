namespace EplanMcp.Spikes.S36.Part2;

// Keeps native wrappers alive once per owner; newly-created owners must be released BEFORE rollback deletes them.
internal sealed class PropertyListCache<T> : IDisposable where T : class, IDisposable
{
    private readonly Dictionary<string, (string Owner, T List)> _entries = new(StringComparer.Ordinal);
    private HashSet<string>? _baseline;
    private bool _rollbackPrepared;
    internal bool TryGetValue(string key, out T list)
    {
        if (_rollbackPrepared) { throw new InvalidOperationException("Property access is forbidden between cache cleanup and rollback completion."); }
        bool found = _entries.TryGetValue(key, out var entry);
        list = found ? entry.List : null!;
        return found;
    }
    internal void Add(string key, string owner, T list)
    {
        if (_rollbackPrepared) { throw new InvalidOperationException("Cannot cache a wrapper after rollback preparation."); }
        _entries.Add(key, (owner, list));
    }
    internal void BeginWrite(IEnumerable<string> owners)
    {
        if (_baseline is not null) { throw new InvalidOperationException("A write scope is already active."); }
        _baseline = new HashSet<string>(owners.Concat(_entries.Values.Select(e => e.Owner)), StringComparer.Ordinal);
        _rollbackPrepared = false;
    }
    internal (int Released, Exception[] Errors) PrepareRollback()
    {
        if (_baseline is null) { return (0, []); }
        _rollbackPrepared = true;
        string[] keys = _entries.Where(e => !_baseline.Contains(e.Value.Owner)).Select(e => e.Key).ToArray();
        var errors = new List<Exception>();
        foreach (string key in keys)
        {
            T list = _entries[key].List;
            _entries.Remove(key); // Never leave the wrapper queued for Dispose after native deletion, even if release fails.
#pragma warning disable CA1816 // A deleted native owner's wrapper must never finalize later; this cache owns its cleanup boundary.
            try { list.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
            finally { GC.SuppressFinalize(list); }
#pragma warning restore CA1816
        }
        return (keys.Length, errors.ToArray());
    }
    internal void EndWrite()
    {
        _baseline = null;
        _rollbackPrepared = false;
    }
    internal void Abandon()
    {
        // Native state is unknown. Do not read/dispose any retained wrapper, even from a later finally/finalizer.
#pragma warning disable CA1816 // This cache owns the cleanup boundary; unknown native owners cannot safely finalize later.
        foreach (var entry in _entries.Values) { GC.SuppressFinalize(entry.List); }
#pragma warning restore CA1816
        _entries.Clear();
        _baseline = null;
        _rollbackPrepared = true;
    }
    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var entry in _entries.Values)
        {
            try { entry.List.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        _entries.Clear();
        if (errors.Count > 0) { throw new AggregateException("Property-list release failed.", errors); }
    }
}
