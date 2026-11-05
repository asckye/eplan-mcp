using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel.Graphics
{
    internal sealed class HyperLink(long oid = 106) : TextBase(oid)
    {
        public string Link { get; set; } = "";
        public void Create(Page page, string link, MultiLangString contents)
        { Link = link; Page = page; Contents = new(contents.GetAsString()); page.AllPlacements = [.. page.AllPlacements, this]; }
    }

    internal sealed class QRCode(long oid = 107) : Rectangle(oid)
    {
        public string Text { get; set; } = "";
        public Exception? TextError { get; set; }
        public string GetQRCode() => TextError is { } error ? throw error : Text;
        public void Create(Page page, PointD point, double size, string text)
        { Page = page; base.Create(page); Location = point; SetArea(point, new(point.X + size, point.Y + size)); Text = text; }
    }

    internal sealed class Spline(long oid = 108) : PolyLine(oid);

    internal sealed class DimensionItem(long oid = 109) : GraphicalPlacement(oid)
    {
        public string DimensionText { get; set; } = "60 mm";
        public PointD FirstPoint { get; private set; }
        public PointD SecondPoint { get; private set; }
        public PointD EndPoint { get; private set; }
        public void Create(Page page, PointD first, PointD second, PointD end)
        { Page = page; FirstPoint = first; SecondPoint = second; EndPoint = end; Location = end; page.AllPlacements = [.. page.AllPlacements, this]; }
        public void CreateAngular(Page page, PointD first, PointD second, PointD end)
        { Create(page, first, second, end); DimensionText = "45°"; }
    }

    internal sealed class DimensionCircle(long oid = 110) : GraphicalPlacement(oid)
    {
        public string DimensionText { get; set; } = "12 mm";
        public void Create(Page page, Arc arc, PointD end)
        { Assert.Equal("Circle", arc.Kind); Page = page; Location = end; page.AllPlacements = [.. page.AllPlacements, this]; }
    }

}

namespace Eplan.EplApi.DataModel
{
    internal sealed class DimensionGroup : Group
    {
        public DimensionGroup() : base(113) { }
        public static class Enums { public enum DimensionType { BaseLine, Incremental, Continued } }
        public void Create(Page page, PointD[] points, PointD end, Enums.DimensionType type)
        {
            Page = page;
            var items = new List<Placement>();
            for (int i = 1; i < points.Length; i++)
            {
                var item = new Graphics.DimensionItem(113 + i);
                item.Create(page, points[i - 1], points[i], end);
                items.Add(item);
            }
            SubPlacements = items.ToArray();
            page.AllPlacements = [.. page.AllPlacements, this];
        }
    }
}
