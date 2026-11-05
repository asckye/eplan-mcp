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



}

namespace Eplan.EplApi.DataModel
{
}
