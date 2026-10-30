using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel.MasterData;

namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class Cable : Placement
{
    private readonly FakeCableStore _store;
    private readonly CableState _state;
    private readonly Action _guard;
    public Cable() : this(FakeCableStore.Current!, new()) { }
    public Cable(FakeCableStore store, CableState state) : base(10, 23)
    { _store = store; _state = state; _guard = store.Guard(); }
    public void Create(Page page, SymbolVariant variant, PointD start, PointD end)
    { _guard(); _state.Page = page; _state.Area = new(start, end); _store.Cables.Add(_state); }
    public string Name { get { _guard(); return _state.Name; } set { _guard(); _state.Name = value; } }
    public string VisibleName => Name;
    public Page Page { get { _guard(); return _state.Page; } }
    public FunctionCategory FunctionCategory { get { _guard(); return FunctionCategory.Cable; } }
    public RectangleD GetLogicalArea() { _guard(); return _state.Area; }
    public FunctionPropertyList Properties { get { _guard(); return new(_store, _state.Properties); } }
    public FunctionBasePropertyList NameParts { get { _guard(); return new(_guard) { DESIGNATION_LOCATION = Name }; } }
    public Connection[] CableConnections { get { _guard(); return _store.FindConnections(); } }
    public Connection[] FunctionTemplates { get { _guard(); return _state.Templates.Select(t => new Connection(_store, t)).ToArray(); } }
    public GraphicalConnection[] GraphicalConnections { get { _guard(); return [new()]; } }
    public void AddLocalFunctionTemplates(Connection[] runs)
    { _guard(); _state.Templates = runs.Select(r => r.State).ToArray(); }
}
