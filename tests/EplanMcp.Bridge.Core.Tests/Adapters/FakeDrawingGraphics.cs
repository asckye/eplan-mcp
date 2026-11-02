using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel.Graphics;

internal abstract class TextBase(long oid) : Placement(oid)
{
    public int UndockCalls { get; private set; }
    public double Rotation { get; set; }
    public MultiLangString Contents { get; set; } = new("en_US@Test;");
    public double Height { get; set; } = 3;
    public bool IsDocked { get; set; }
    public bool ReadOnly { get; init; }
    public void UndockText()
    {
        UndockCalls++;
        if (IsReadOnly)
        { throw new BaseException("数据库写保护"); }
        if (ReadOnly)
        { throw new BaseException(); }
        IsDocked = false;
    }
    public bool IsVisible { get; set; }
}

internal sealed class Text(long oid = 99) : TextBase(oid)
{
    public void Create(Page page, MultiLangString contents, double height)
    {
        Height = height;
        Contents = new(contents.GetAsString());
        page.AllPlacements = [.. page.AllPlacements, this];
    }
}

internal class PropertyPlacement(long oid = 99, Placement? parent = null) : TextBase(oid)
{
    public Placement Parent { get; private set; } = parent!;
    public string Display { get; set; } = "-K1";
    public int DisplayedId { get; set; } = 20214;
    public AnyPropertyId DisplayedProperty => new(DisplayedId);
    public string GetDisplayString() => Display;
    public void Create(Function owner, AnyPropertyId id)
    {
        Parent = owner;
        DisplayedId = id.AsInt;
        using MultiLangString text = ((EObjects.TerminalStrip)owner).Properties.FUNC_TEXT.ToMultiLangString();
        Display = id.AsInt == 20214 ? owner.Name : text.GetStringToDisplay(ISOCode.Language.L___);
        owner.PropertyPlacements = [.. owner.PropertyPlacements, this];
    }
    public PointD[] GetBoundingBox(bool relative)
    {
        Assert.False(relative);
        return GetBoundingBox();
    }
}

internal sealed class ContextPropertyPlacement(long oid, Placement parent) : PropertyPlacement(oid, parent);

internal sealed class Line(long oid = 100) : Placement(oid)
{
    public PointD StartPoint { get; set; }
    public PointD EndPoint { get; set; }
    public void Create(Page page, PointD start, PointD end)
    {
        StartPoint = start;
        EndPoint = end;
        page.AllPlacements = [.. page.AllPlacements, this];
        page.AfterGraphicCreate?.Invoke();
    }
}

internal sealed class Rectangle(long oid = 101) : Placement(oid)
{
    public PointD LowerLeftCorner { get; private set; }
    public PointD UpperRightCorner { get; private set; }
    public void Create(Page page) => page.AllPlacements = [.. page.AllPlacements, this];
    public void SetArea(PointD lower, PointD upper)
    {
        Assert.True(LowerLeftCorner == default && UpperRightCorner == default);
        LowerLeftCorner = lower;
        UpperRightCorner = upper;
    }
}

internal struct Pen
{
    public int StyleId { get; set; }
}

internal sealed class GraphicalPlacement(long oid) : Placement(oid)
{
    private Pen _pen;
    public bool RejectStyle { get; set; }
    public Action? OnSet { get; set; }
    public Pen Pen
    {
        get => _pen;
        set { if (!RejectStyle) { _pen = value; } OnSet?.Invoke(); }
    }
}
