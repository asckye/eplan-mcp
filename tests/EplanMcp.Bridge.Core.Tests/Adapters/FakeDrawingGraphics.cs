using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel.Graphics;

internal abstract class TextBase(long oid) : Placement(oid)
{
    public MultiLangString Contents { get; set; } = new("en_US@Test;");
    public double Height { get; set; } = 3;
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

internal sealed class PropertyPlacement(long oid, Placement parent) : TextBase(oid)
{
    public Placement Parent { get; } = parent;
    public string Display { get; set; } = "-K1";
    public int DisplayedId { get; set; } = 20214;
    public AnyPropertyId DisplayedProperty => new(DisplayedId);
    public string GetDisplayString() => Display;
    public PointD[] GetBoundingBox(bool relative)
    {
        Assert.False(relative);
        return GetBoundingBox();
    }
}

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
