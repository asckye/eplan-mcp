using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel;

internal sealed class LocationBox(long oid) : SymbolReference(oid)
{
    [ThreadStatic]
    public static Func<object, Page, SymbolReference>? Factory;
    public static SymbolReference CreateBox(object variant, Page page) => Factory!(variant, page);
    public Page Page { get; set; } = null!;
    public BoxSymbolVariant SymbolVariant { get; set; } = new();
    public Placement? Graphics { get; set; }
    public bool WrongArea { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public FunctionBasePropertyList? AssignedList { get; private set; }
    public FunctionBasePropertyList? ReadList { get; private set; }
    public FunctionBasePropertyList NameParts
    {
        get => ReadList = new() { DESIGNATION_LOCATION = LocationName };
        set { AssignedList = value; LocationName = value.DESIGNATION_LOCATION.Text; }
    }
    public Placement? GetGraphics() => Graphics;
    private RectangleD _area;
    public void SetLogicalArea(PointD a, PointD b) => _area = new(a, b);
    public RectangleD GetLogicalArea() => WrongArea ? new(new(0, 0), new(4, 4)) : _area;
}

internal sealed class FunctionBasePropertyList(Action? checkAlive = null) : IDisposable
{
    private PropertyValue _location = new("");
    public PropertyValue DESIGNATION_LOCATION { get { checkAlive?.Invoke(); return _location; } set { checkAlive?.Invoke(); _location = value; } }
    public bool Disposed { get; private set; }
    public void Dispose() { checkAlive?.Invoke(); Disposed = true; }
}

internal sealed class PropertyValue(string text, Action? checkAlive = null) : IDisposable
{
    public bool Disposed { get; private set; }
    public string Text { get { checkAlive?.Invoke(); return text; } }
    public bool IsEmpty => Text.Length == 0;
    public override string ToString() => Text;
    public static implicit operator PropertyValue(string text) => new(text);
    public Eplan.EplApi.Base.MultiLangString ToMultiLangString() => new(Text);
    public static implicit operator PropertyValue(Eplan.EplApi.Base.MultiLangString value) => new(value.GetAsString());
    public void Dispose() { checkAlive?.Invoke(); Disposed = true; }
}

internal sealed class BoxSymbolVariant
{
    public string Library { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Variant { get; set; }
    public string SymbolLibraryName => Library;
    public string SymbolName => Name;
    public int VariantNr => Variant;
}
