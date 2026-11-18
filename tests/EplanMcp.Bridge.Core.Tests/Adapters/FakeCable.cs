using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel.MasterData;

namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class Cable : Function
{
    private readonly FakeCableStore _store;
    private readonly CableState _state;
    private readonly Action _guard;
    public Cable() : this(FakeCableStore.Current!, new()) { }
    public Cable(FakeCableStore store, CableState state) : base(store.IdentifierOf(state))
    { IdentifierType = 23; _store = store; _state = state; _guard = store.Guard(); }
    public void Create(Page page, SymbolVariant variant, PointD start, PointD end)
    { _guard(); _state.Page = page; _state.Area = new(start, end); _store.Cables.Add(_state); }
    public new string Name
    {
        get
        {
            _guard();
            return _state.Name;
        }
        set
        {
            _guard();
            // A55: EPLAN stores a bare cable tag with an empty location segment.
            _state.Name = value.StartsWith('-') && _state.Page is { } page && EplanMcp.Contracts.DrawingNames.DeviceStructureOf(page.Name).Length > 0
                ? "+" + value
                : value;
        }
    }

    // A81: an API-created cable starts with an empty visible name until adjusted.
    public new string VisibleName
    {
        get
        {
            _guard();
            return _state.VisibleName;
        }
    }

    public void AdjustVisibleName(Page page)
    {
        // A81: these two adjusted names were observed on the same page; the marker's cause is unknown.
        if (page.Name == "=GAA+A1&EFS1/3" && Name is "+A1-W3" or "+A1-W4")
        {
            _state.VisibleName = Name == "+A1-W3" ? "-W3" : ">-W4";
        }
    }
    public new Page Page { get { _guard(); return _state.Page; } }
    public new FunctionCategory FunctionCategory { get { _guard(); return FunctionCategory.Cable; } }
    public RectangleD GetLogicalArea() { _guard(); return _state.Area; }
    public new FunctionPropertyList Properties { get { _guard(); return new(_store, _state.Properties); } }
    public FunctionBasePropertyList NameParts { get { _guard(); return new(_guard) { DESIGNATION_LOCATION = Name }; } }
    // A64/A55: only wires belonging to this cable are members.
    public Connection[] CableConnections
    {
        get
        {
            _guard();
            return [.. _store.FindConnections().Where(connection => connection.State.Cable == _state)];
        }
    }
    public Connection[] FunctionTemplates { get { _guard(); return _state.Templates.Select(t => new Connection(_store, t)).ToArray(); } }
    public new GraphicalConnection[] GraphicalConnections { get { _guard(); return [new()]; } }
    // UNVERIFIED (2026-11-18 survey E4-22): Local templates share the writable connection state of the source wire.
    public void AddLocalFunctionTemplates(Connection[] runs)
    { _guard(); _state.Templates = runs.Select(r => r.State).ToArray(); }
}
