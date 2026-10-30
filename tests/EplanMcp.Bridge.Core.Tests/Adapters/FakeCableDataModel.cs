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
    public List<CableState> Cables { get; } = [];
    public ConnectionState Wire { get; } = new();
    public Connection[] FindConnections() => [new(this, Wire)];
    public Placement[] FindPlacements() { PlacementEnumerations++; return Cables.Select(c => (Placement)new Cable(this, c)).ToArray(); }
    public Action Guard()
    {
        int generation = Generation;
        return () => ObjectDisposedException.ThrowIf(generation != Generation, this);
    }
}

internal sealed class CableState
{
    public string Name { get; set; } = "";
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
    public void Dispose()
    {
        CheckAlive();
        Disposed = true;
        if (_store is not null)
        { _store.OpenLists--; }
    }
}

internal sealed class ConnectionPropertyList(FakeCableStore? store, Dictionary<int, string> values) : NativeCableList(store)
{
    public PropertyValue this[Properties.Connection id]
    {
        get { CheckAlive(); return new(values.GetValueOrDefault((int)id, ""), CheckAlive); }
        set { CheckAlive(); values[(int)id] = value.Text; }
    }
    public PropertyValue this[AnyPropertyId id] => this[(Properties.Connection)id.AsInt];
}

internal class Connection(FakeCableStore? store = null, ConnectionState? state = null)
{
    private readonly Action _guard = store?.Guard() ?? (() => { });
    private readonly ConnectionState _state = state ?? new();
    private Page? _page = store?.Page;
    private Pin? _startPin = store is null ? null : new(1, 0);
    private Pin? _endPin = store is null ? null : new(2, 0);
    public ConnectionState State => _state;
    public long ObjectIdentifier { get { _guard(); return 9; } }
    public Page? Page { get { _guard(); return _page; } set { _guard(); _page = value; } }
    public Pin? StartPin { get { _guard(); return _startPin; } set { _guard(); _startPin = value; } }
    public Pin? EndPin { get { _guard(); return _endPin; } set { _guard(); _endPin = value; } }
    public bool IsTemplate { get { _guard(); return true; } }
    public ConnectionPropertyList Properties { get { _guard(); return new(store, _state.Properties); } }
    public ConnectionDefinitionPoint[] ConnectionDefPoints { get { _guard(); return store is not null && _state.Cable is { } cable ? [new(store, cable)] : []; } }
    public FunctionBasePropertyList CableNameParts
    {
        set { _guard(); _state.Properties[20005] = value.DESIGNATION_LOCATION.Text; }
    }
}

internal sealed class Pin
{
    public Pin() { }
#pragma warning disable CA2000 // The fake project owns the pin's function (A39/A55).
    public Pin(long oid, int index) { ParentFunction = new(oid); Index = index; }
#pragma warning restore CA2000
    public Function? ParentFunction { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }
}

internal sealed class ConnectionDefinitionPoint(FakeCableStore store, CableState cable)
{
    private readonly Action _guard = store.Guard();
    public Cable CableDefinitionLine { get { _guard(); return new(store, cable); } }
    public string Name { get { _guard(); return cable.Name; } set { _guard(); Assert.Equal(cable.Name, value); } }
    public string VisibleName { get { _guard(); return cable.Name; } set { _guard(); Assert.Equal(cable.Name, value); } }
}

internal sealed class GraphicalConnection
{
    public SymbolReference StartSymbolRef { get; } = new(1);
    public SymbolReference EndSymbolRef { get; } = new(2);
}
