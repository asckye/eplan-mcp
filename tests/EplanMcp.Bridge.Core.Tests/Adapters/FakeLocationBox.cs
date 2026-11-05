using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel;

internal sealed class LocationBox(long oid) : FunctionBase(oid)
{
    [ThreadStatic]
    public static Func<object, Page, SymbolReference>? Factory;
    public static SymbolReference CreateBox(object variant, Page page) => Factory!(variant, page);
    public new BoxSymbolVariant SymbolVariant { get; set; } = new();
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
    public PropertyValue DESIGNATION_PLANT { get; set; } = new("");
    public PropertyValue FUNC_CODE { get; set; } = new("");
    public PropertyValue FUNC_COUNTER { get; set; } = new("");
    public bool Disposed { get; private set; }
    public void Dispose() { checkAlive?.Invoke(); Disposed = true; }
}

internal sealed class PropertyValue(string text, Action? checkAlive = null) : IDisposable
{
    public bool Disposed { get; private set; }
    public string Text { get { checkAlive?.Invoke(); return text; } }
    public bool IsEmpty => Text.Length == 0;
    public override string ToString() => Text;
    public int ToInt() => int.Parse(Text, System.Globalization.CultureInfo.InvariantCulture);
    public static implicit operator PropertyValue(int value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static implicit operator PropertyValue(string text) => new(text);
    public Eplan.EplApi.Base.MultiLangString ToMultiLangString() => new(Text.Contains('@', StringComparison.Ordinal) ? Text : "??_??@" + Text + ";");
    public static implicit operator PropertyValue(Eplan.EplApi.Base.MultiLangString value) => new(value.GetAsString());
    public void Dispose() { checkAlive?.Invoke(); Disposed = true; }
}

internal sealed class BoxSymbolVariant : MasterData.SymbolVariant
{
    public BoxSymbolVariant() { SymbolLibraryName = string.Empty; SymbolName = string.Empty; }
    public string Library { get => SymbolLibraryName; set => SymbolLibraryName = value; }
    public string Name { get => SymbolName; set => SymbolName = value; }
    public int Variant { get => VariantNr; set => VariantNr = value; }
}
