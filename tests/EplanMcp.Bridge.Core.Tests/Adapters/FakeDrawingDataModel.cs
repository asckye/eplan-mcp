using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel.Graphics;

namespace Eplan.EplApi.DataModel;

internal class Placement(long oid, int type = 4) : IDisposable
{
    public long ObjectIdentifier { get; } = oid;
    public string Prefix { get; set; } = "53";
    public PointD Location { get; set; }
    public PointD[] Bounds { get; set; } = [new(0, 0), new(10, 10)];
    public Exception? BoundsError { get; set; }
    public string ToStringIdentifier() => $"{Prefix}/{type}/{ObjectIdentifier}/0";
    public PointD[] GetBoundingBox() => BoundsError is { } error ? throw error : Bounds;
    public void Dispose() => throw new InvalidOperationException("Borrowed placement must never be disposed.");
}

internal sealed class Group(long oid, params Placement[] children) : Placement(oid)
{
    public Placement[] SubPlacements { get; } = children;
}

internal interface IPropertyPlacementsContainer
{
    public PropertyPlacement[] PropertyPlacements { get; }
}

internal class SymbolReference(long oid) : Placement(oid, 17)
{
    public PropertyPlacement[] PropertyPlacements { get; set; } = [];
}

internal class FunctionBase(long oid) : SymbolReference(oid)
{
    public string Name { get; set; } = "+A1-K1";
}

internal sealed class Function(long oid) : FunctionBase(oid);

internal sealed class PlotFrame(params Placement[] children)
{
    public Placement[] SubPlacements { get; } = children;
    public PointD Size { get; set; } = new(420, 297);
}

internal sealed class Page : IDisposable
{
    public string Name { get; set; } = "+A1/1";
    public double GridSize { get; set; } = 4;
    public PointD StoredSize { get; set; } = new(420, 297);
    public Exception? SizeError { get; set; }
    public PointD Size => SizeError is { } error ? throw error : StoredSize;
    public PlotFrame? PlotFrame { get; set; }
    public Placement[] AllPlacements { get; set; } = [];
    public void Dispose() => throw new InvalidOperationException("Borrowed page must never be disposed.");
}

internal sealed class Project(params Page[] pages)
{
    public Page[] Pages { get; } = pages;
}

internal sealed class DMObjectsFinder(Project project) : IDisposable
{
    public Page[] GetPages(PagesFilter filter) => [.. project.Pages.Where(p => p.Name.StartsWith(filter.Name, StringComparison.OrdinalIgnoreCase))];
    public void Dispose() { }
}

internal sealed class PagesFilter : IDisposable
{
    public string Name { get; set; } = string.Empty;
    public bool ExactNameMatching { get; set; }
    public void Dispose() { }
}
