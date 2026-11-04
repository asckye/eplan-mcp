using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel.MasterData;

namespace Eplan.EplApi.DataModel;

internal sealed class BoxedDevice(long oid = 40) : Function(oid)
{
    [ThreadStatic]
    public static Action<BoxedDevice>? OnCreate;
    private SymbolVariant _symbol = new();
    private bool _created;

    /// <summary>A86: the native setter throws S063080 on an object that has not been created.</summary>
    public new SymbolVariant SymbolVariant
    {
        get => _symbol;
        set
        {
            if (!_created)
            { throw new InvalidOperationException("S063080: the object has not been created."); }
            _symbol = value;
            base.SymbolVariant.Library = value.SymbolLibraryName;
            base.SymbolVariant.Name = value.SymbolName;
            base.SymbolVariant.Variant = value.VariantNr;
        }
    }

    public bool WrongArea { get; set; }
    private RectangleD _area;

    public void Create(Page page)
    {
        base.Create(page, _symbol);
        _created = true;
        IsMainFunction = true;
        FunctionCategory = FunctionCategory.Blackbox;
        OnCreate?.Invoke(this);
    }

    public void SetLogicalArea(PointD lowerLeft, PointD upperRight)
    {
        CheckNative?.Invoke();
        _area = new(lowerLeft, upperRight);
        Bounds = [lowerLeft, upperRight];
    }

    public RectangleD GetLogicalArea()
    {
        CheckNative?.Invoke();
        return WrongArea ? new(new(0, 0), new(1, 1)) : _area;
    }

    public Function[] FunctionsInside => [.. Page.AllPlacements.OfType<Function>().Where(function => !ReferenceEquals(function, this) && IsFunctionInside(function))];

    public bool IsFunctionInside(Function function)
    {
        CheckNative?.Invoke();
        PointD point = function.Location;
        return point.X >= _area.LowerLeft.X && point.X <= _area.UpperRight.X
            && point.Y >= _area.LowerLeft.Y && point.Y <= _area.UpperRight.Y;
    }
}
