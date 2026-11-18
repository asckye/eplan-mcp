using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel.EObjects;

namespace Eplan.EplApi.DataModel;

// Each enumeration returns new handles; ending a scope invalidates every older handle and list (A55).
internal sealed class FakeCableStore
{
    [ThreadStatic]
    public static FakeCableStore? Current;
    public int Generation { get; set; }
    public int OpenLists { get; set; }
    public int ListsAcquired { get; set; }
    public int PlacementEnumerations { get; private set; }
    public Page Page { get; set; } = new();
    private readonly Dictionary<CableState, long> _identifiers = [];
    public List<CableState> Cables { get; } = [];

    // A32: different cables in one project have distinct object numbers that survive re-enumeration.
    public long IdentifierOf(CableState state)
    {
        if (!_identifiers.TryGetValue(state, out long id))
        {
            id = 10L + _identifiers.Count;
            _identifiers.Add(state, id);
        }
        return id;
    }
    public List<ConnectionState> Wires { get; } = [new()];
    public ConnectionState Wire => Wires[0];
    public Connection[] FindConnections() => [.. Wires.Select(state => new Connection(this, state))];
    public Placement[] FindPlacements() { PlacementEnumerations++; return Cables.Select(c => (Placement)new Cable(this, c)).ToArray(); }
    public Action Guard()
    {
        int generation = Generation;
        // CONTRADICTS A55: kept because CableNativeLifetimeTests must catch stale-handle misuse; EPLAN terminates with an access violation; see the survey report.
        return () => ObjectDisposedException.ThrowIf(generation != Generation, this);
    }
}

internal sealed class CableState
{
    public string Name { get; set; } = "";
    public string VisibleName { get; set; } = "";
    public Page Page { get; set; } = null!;
    public RectangleD Area { get; set; }
    public Dictionary<int, string> Properties { get; } = [];
    public ConnectionState[] Templates { get; set; } = [];
}

internal sealed class ConnectionState
{
    public Dictionary<int, string> Properties { get; } = [];
    public CableState? Cable { get; set; }
}

internal abstract class NativeCableList : IDisposable
{
    private readonly FakeCableStore? _store;
    private readonly Action? _guard;
    public bool Disposed { get; private set; }
    // PLC/terminal fixtures track disposal without transaction generations.
    protected NativeCableList(FakeCableStore? store)
    {
        _store = store;
        _guard = store?.Guard();
        if (store is not null)
        { store.OpenLists++; store.ListsAcquired++; }
    }
    protected void CheckAlive()
    { _guard?.Invoke(); ObjectDisposedException.ThrowIf(_store is not null && Disposed, this); }
    // UNVERIFIED (2026-11-18 survey E4-24): Disposing a list twice fails only for store-backed lists in the current generation.
    public void Dispose()
    {
        CheckAlive();
        Disposed = true;
        if (_store is not null)
        { _store.OpenLists--; }
    }
}

internal sealed class ConnectionPropertyList(FakeCableStore? store, ConnectionState state) : NativeCableList(store)
{
    private readonly bool _storeBacked = store is not null;

    public PropertyValue this[Properties.Connection id]
    {
        get
        {
            CheckAlive();
            // A55: unallocated wire property 20005 reads as a bare plus.
            string fallback = (int)id == 20005 && state.Cable is null && _storeBacked ? "+" : "";
            return new(state.Properties.GetValueOrDefault((int)id, fallback), CheckAlive);
        }
        set
        {
            CheckAlive();
            state.Properties[(int)id] = value.Text;
        }
    }
    public PropertyValue this[AnyPropertyId id] => this[(Properties.Connection)id.AsInt];
}

internal class Connection(FakeCableStore? store = null, ConnectionState? state = null)
    : StorableObject
{
    private readonly Action _guard = store?.Guard() ?? (() => { });
    private readonly ConnectionState _state = state ?? new();
    private Page? _page = store?.Page;
    private Pin? _startPin = store is null ? null : new(1, 0);
    private Pin? _endPin = store is null ? null : new(2, 0);
    public ConnectionState State => _state;
    // UNVERIFIED (2026-11-18 survey E4-30): Every wire and template reports object ID 9 and IsTemplate true.
    public long ObjectIdentifier { get { _guard(); return 9; } }
    public Page? Page { get { _guard(); return _page; } set { _guard(); _page = value; } }
    public virtual Pin? StartPin { get { _guard(); return _startPin; } set { _guard(); _startPin = value; } }
    public virtual Pin? EndPin { get { _guard(); return _endPin; } set { _guard(); _endPin = value; } }
    public bool IsTemplate { get { _guard(); return true; } }
    public ConnectionPropertyList Properties { get { _guard(); return new(store, _state); } }
#pragma warning disable CA2000 // Returned native definition points are project-owned, as are the fake placements (A39).
    public ConnectionDefinitionPoint[] ConnectionDefPoints { get { _guard(); return store is not null && _state.Cable is { } cable ? [new(store, cable)] : []; } }
#pragma warning restore CA2000
    public FunctionBasePropertyList CableNameParts
    {
        // UNVERIFIED (2026-11-18 survey E4-25): Binding copies only DESIGNATION_LOCATION into property 20005.
        set { _guard(); _state.Properties[20005] = value.DESIGNATION_LOCATION.Text; }
    }
}

// EPLAN raises these when a pin carries no connection designation (S063111) or an indexed one (S063172).
internal sealed class EmptyPropertyException(string message) : System.Exception(message);
internal sealed class IndexedPropertyException(string message) : System.Exception(message);

internal sealed class Pin
{
    public Pin() { }
#pragma warning disable CA2000 // The fake project owns the pin's function (A39/A55).
    public Pin(long oid, int index) { ParentFunction = new(oid); Index = index; }
#pragma warning restore CA2000
    public Function? ParentFunction { get; set; }
    /// <summary>The native failure EPLAN raises for this pin's connection designation, if any.</summary>
    public System.Exception? NameFailure { get; set; }
    public string? Name { get => NameFailure is null ? field : throw NameFailure; set; }
    public int Index { get; set; }
}

internal sealed class ConnectionDefinitionPoint(FakeCableStore store, CableState cable) : SymbolReference(59)
{
    private readonly Action _guard = store.Guard();
    public Cable CableDefinitionLine { get { _guard(); return new(store, cable); } }
    // UNVERIFIED (2026-11-18 survey E4-31): A definition point reads the cable's full name even without a write.
    public string Name { get { _guard(); return cable.Name; } set { _guard(); Assert.Equal(cable.Name, value); } }
    // A81: a cable core definition point reads back an empty visible name after assignment.
    public string VisibleName
    {
        get
        {
            _guard();
            return string.Empty;
        }
        set => _guard();
    }
}

internal sealed class GraphicalConnection
{
    public SymbolReference StartSymbolRef { get; set; } = new(1);
    public SymbolReference EndSymbolRef { get; set; } = new(2);
    public PinBase StartConnectionPoint { get; set; } = new();
    public PinBase EndConnectionPoint { get; set; } = new();
}
