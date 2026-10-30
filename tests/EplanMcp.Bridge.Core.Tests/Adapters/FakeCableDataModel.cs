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
    private readonly FakeCableStore _store;
    private readonly Action _guard;
    private bool _disposed;
    protected NativeCableList(FakeCableStore store)
    { _store = store; _guard = store.Guard(); store.OpenLists++; store.ListsAcquired++; }
    protected void CheckAlive() { _guard(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose() { CheckAlive(); _disposed = true; _store.OpenLists--; }
}

internal sealed class FunctionPropertyList(FakeCableStore store, Dictionary<int, string> values) : NativeCableList(store)
{
    public PropertyValue this[Properties.Function id]
    {
        get { CheckAlive(); return new(values.GetValueOrDefault((int)id, ""), CheckAlive); }
        set { CheckAlive(); values[(int)id] = value.Text; }
    }
}

internal sealed class ConnectionPropertyList(FakeCableStore store, Dictionary<int, string> values) : NativeCableList(store)
{
    public PropertyValue this[Properties.Connection id]
    {
        get { CheckAlive(); return new(values.GetValueOrDefault((int)id, ""), CheckAlive); }
        set { CheckAlive(); values[(int)id] = value.Text; }
    }
    public PropertyValue this[AnyPropertyId id] => this[(Properties.Connection)id.AsInt];
}

internal static class Properties
{
    public enum Function { }
    public enum Connection { }
}

internal sealed class Connection(FakeCableStore store, ConnectionState state)
{
    private readonly Action _guard = store.Guard();
    public ConnectionState State => state;
    public long ObjectIdentifier { get { _guard(); return 9; } }
    public Page Page { get { _guard(); return store.Page; } }
    public Pin StartPin { get { _guard(); return new(1, 0); } }
    public Pin EndPin { get { _guard(); return new(2, 0); } }
    public bool IsTemplate { get { _guard(); return true; } }
    public ConnectionPropertyList Properties { get { _guard(); return new(store, state.Properties); } }
    public ConnectionDefinitionPoint[] ConnectionDefPoints { get { _guard(); return state.Cable is { } cable ? [new(store, cable)] : []; } }
    public FunctionBasePropertyList CableNameParts
    {
        set { _guard(); state.Properties[20005] = value.DESIGNATION_LOCATION.Text; }
    }
}

internal sealed class Pin(long oid, int index)
{
    public SymbolReference ParentFunction { get; } = new(oid);
    public int Index { get; } = index;
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
