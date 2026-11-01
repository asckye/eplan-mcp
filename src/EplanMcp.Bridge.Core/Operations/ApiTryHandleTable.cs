using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Process-local borrowed objects. The adapter owns this table; callbacks run only on the main thread.</summary>
public sealed class ApiTryHandleTable(Func<DateTime> clock)
{
    private readonly Dictionary<string, (string Session, object Value, string Type, string Source, Func<string?> Owner, bool ReadOnly, DateTime Used, long Order)> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _order;
    public void Clear() { lock (_gate) { _entries.Clear(); } }
    public void Sweep(Func<string, bool> alive)
    {
        lock (_gate)
        {
            foreach (string key in _entries.Where(p => clock() - p.Value.Used >= TimeSpan.FromMinutes(10) || !alive(p.Value.Session)).Select(p => p.Key).ToArray())
            { _entries.Remove(key); }
        }
    }
    private static string Token() { byte[] bytes = new byte[16]; using var random = System.Security.Cryptography.RandomNumberGenerator.Create(); random.GetBytes(bytes); return "h:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    public ApiObjectHandle Add(string session, object value, string type, string source, Func<string?> owner, bool readOnly = false)
    {
        lock (_gate)
        {
            if (_entries.Values.Count(e => e.Session == session) >= 500)
            { _entries.Remove(_entries.Where(p => p.Value.Session == session).OrderBy(p => p.Value.Order).First().Key); }
            string token = Token();
            _entries.Add(token, (session, value, type, source, owner, readOnly, clock(), ++_order));
            return new() { Handle = token, Type = type };
        }
    }
    public (object Value, Func<string?> Owner, bool ReadOnly) Resolve(string session, ApiObjectHandle handle, string expected, string project, Func<object, string, bool> compatible, Func<string, bool> scratch, out string description, bool write = false)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(handle.Handle, out var entry) || entry.Session != session || clock() - entry.Used >= TimeSpan.FromMinutes(10))
            { throw new BridgeErrorException(ErrorReason.PlanStale, "The object handle expired; obtain it again in this session."); }
            if (entry.Type != handle.Type || !compatible(entry.Value, expected))
            { throw new BridgeErrorException(ErrorReason.InvalidArgument, "The handle type does not match the generated signature."); }
            if (write && entry.ReadOnly)
            { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Settings handles are read-only."); }
            string? owner;
            try
            { owner = entry.Owner(); }
            catch (Exception ex) { _entries.Remove(handle.Handle); throw new BridgeErrorException(ErrorReason.PlanStale, "The borrowed object is no longer valid; obtain a fresh handle.", ex); }
            if (owner is null || !_entries.ContainsKey(handle.Handle))
            { _entries.Remove(handle.Handle); throw new BridgeErrorException(ErrorReason.PlanStale, "The owning object is no longer valid; obtain a fresh handle."); }
            if (!ProjectResolver.SamePath(project, owner) || !scratch(owner))
            { throw new BridgeErrorException(ErrorReason.PolicyDenied, "The handle must still belong to the approved scratch project."); }
            description = entry.Type + " returned by " + entry.Source;
            entry.Used = clock();
            _entries[handle.Handle] = entry;
            return (entry.Value, entry.Owner, entry.ReadOnly);
        }
    }
}
