using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel.Graphics;

internal abstract class TextBase(long oid) : Placement(oid)
{
    public MultiLangString Contents { get; set; } = new("en_US@Test;");
}

internal sealed class Text(long oid) : TextBase(oid);

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

internal sealed class Line(long oid) : Placement(oid)
{
    public PointD StartPoint { get; set; }
    public PointD EndPoint { get; set; }
}

internal sealed class Rectangle(long oid) : Placement(oid);
