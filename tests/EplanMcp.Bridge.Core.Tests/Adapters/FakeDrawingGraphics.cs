using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel.Graphics;

internal abstract class TextBase(long oid) : GraphicalPlacement(oid)
{
    public int UndockCalls { get; private set; }
    public double Rotation { get; set; }
    public enum JustificationType { FromLayer, BaseLineLeft, MiddleLeft, MiddleCenter, BaseLineCenter }
    public JustificationType Justification { get; set; }
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
    public bool IsVisible { get; set; } = true;
}

internal class Text(long oid = 99) : TextBase(oid)
{
    public void Create(Page page, MultiLangString contents, double height)
    {
        Height = height;
        Contents = new(contents.GetAsString());
        page.AllPlacements = [.. page.AllPlacements, this];
    }
}

internal sealed class PathText(long oid = 102) : Text(oid);

internal sealed class PolyLine(long oid = 103) : GraphicalPlacement(oid)
{
    private readonly List<PointD> _points = [];
    public int NumberOfPoints => _points.Count;
    public bool Closed { get; set; }
    public void Create(Page page) => page.AllPlacements = [.. page.AllPlacements, this];
    public PointD GetPointAt(int index) => _points[index];
    public void SetPointAt(int index, ref PointD point)
    {
        if (index == _points.Count)
        { _points.Add(point); }
        else
        { _points[index] = point; }
    }
}

internal sealed class Arc(long oid = 104) : GraphicalPlacement(oid)
{
    public double Radius { get; private set; }
    public double StartAngle { get; private set; }
    public double EndAngle { get; private set; }
    public bool Sector { get; private set; }
    public string Kind { get; set; } = "Arc";
    public double FirstRadius { get; private set; }
    public double SecondRadius { get; private set; }
    public double ElipseAngle { get; private set; }
    public void Create(Page page) => page.AllPlacements = [.. page.AllPlacements, this];
    public void SetArc(PointD center, double radius, double startAngle, double endAngle, bool asPie)
    { Location = center; Radius = radius; StartAngle = startAngle; EndAngle = endAngle; Sector = asPie; }
    public void SetCircle(PointD center, double radius)
    { Location = center; Radius = radius; Kind = "Circle"; }
    public void SetEllipse(PointD center, double rx, double ry, double angle)
    { Location = center; FirstRadius = rx; SecondRadius = ry; ElipseAngle = angle; Kind = "Ellipse"; }
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
        if (owner is EObjects.TerminalStrip strip)
        {
            using MultiLangString text = strip.Properties.FUNC_TEXT.ToMultiLangString();
            Display = id.AsInt == 20214 ? owner.Name : text.GetStringToDisplay(ISOCode.Language.L___);
        }
        else
        { Display = id.AsInt switch { 20213 => "-D3KF5", 20406 => "-X10", 20400 => "I0.1", _ => "" }; }
        owner.PropertyPlacements = [.. owner.PropertyPlacements, this];
    }
    public PointD[] GetBoundingBox(bool relative)
    {
        Assert.False(relative);
        return GetBoundingBox();
    }
}

internal sealed class ContextPropertyPlacement(long oid, Placement parent) : PropertyPlacement(oid, parent);

internal sealed class Line(long oid = 100) : GraphicalPlacement(oid)
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

internal class Rectangle(long oid = 101) : GraphicalPlacement(oid)
{
    public double Angle { get; set; }
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

internal sealed class Image(long oid = 105) : Rectangle(oid)
{
    private string _fileName = "";
    public Exception? FileNameError { get; set; }
    public string FileName { get => FileNameError is { } error ? throw error : _fileName; private set => _fileName = value; }
    public bool CopyToProject { get; private set; }
    public bool Overwrite { get; private set; }
    public void Create(Page page, string fileName, PointD start, PointD end, bool copyToProject, bool overwrite)
    { base.Create(page); SetArea(start, end); FileName = fileName; CopyToProject = copyToProject; Overwrite = overwrite; }
}

internal struct Pen
{
    public short ColorId { get; set; }
    public double Width { get; set; }
    public double PatternLength { get; set; }
    public sbyte LineEndType { get; set; }
    public short StyleId { get; set; }
}

internal class GraphicalPlacement(long oid, int type = 4) : Placement(oid, type)
{
    private Pen _pen;
    public bool RejectStyle { get; set; }
    public Exception? PenError { get; set; }
    public Action? OnSet { get; set; }
    public override Pen Pen
    {
        get => PenError is { } error ? throw error : _pen;
        set { if (!RejectStyle) { _pen = value; } OnSet?.Invoke(); }
    }
}
