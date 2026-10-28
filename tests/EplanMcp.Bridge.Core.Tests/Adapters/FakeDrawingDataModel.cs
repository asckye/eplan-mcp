using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel.Graphics;

namespace Eplan.EplApi.DataModel;

internal class StorableObject(long oid, int type) : IDisposable
{
    public long ObjectIdentifier { get; } = oid;
    public string Prefix { get; set; } = "53";
    public uint DatabaseIdentifier { get; set; } = 53;
    public StorableObject[] CrossReferences { get; set; } = [];
    public Exception? CrossReferenceError { get; set; }
    public StorableObject[] CrossReferencedObjectsAll => CrossReferenceError is { } error ? throw error : CrossReferences;
    public UniversalPropertyList StoredProperties { get; } = new();
    public int PropertyListReads { get; private set; }
    public int Disposals { get; private set; }
    public UniversalPropertyList Properties => ++PropertyListReads == 1 ? StoredProperties : throw new InvalidOperationException("Property list acquired twice.");
    public string ToStringIdentifier() => $"{Prefix}/{type}/{ObjectIdentifier}/0";
    public void Dispose()
    {
        Disposals++;
        throw new InvalidOperationException("Borrowed object must never be disposed.");
    }
}

internal class Placement(long oid, int type = 4) : StorableObject(oid, type)
{
    public Page? Page { get; set; }
    public PointD Location { get; set; }
    public PointD[] Bounds { get; set; } = [new(0, 0), new(10, 10)];
    public Exception? BoundsError { get; set; }
    public PointD[] GetBoundingBox() => BoundsError is { } error ? throw error : Bounds;
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

internal sealed class Function(long oid) : FunctionBase(oid)
{
    public Function[] AllSubFunctions { get; set; } = [];
    public Function? ParentFunction { get; set; }
}

internal sealed class PlotFrame(params Placement[] children)
{
    public Placement[] SubPlacements { get; } = children;
    public PointD Size { get; set; } = new(420, 297);
}

internal sealed class Page(long oid = 1) : StorableObject(oid, 1)
{
    public string Name { get; set; } = "+A1/1";
    public double GridSize { get; set; } = 4;
    public PointD StoredSize { get; set; } = new(420, 297);
    public Exception? SizeError { get; set; }
    public PointD Size => SizeError is { } error ? throw error : StoredSize;
    public PlotFrame? PlotFrame { get; set; }
    public Placement[] AllPlacements { get; set; } = [];
    public Function[] Functions { get; set; } = [];
}

internal sealed class Project(params Page[] pages) : StorableObject(0, 0)
{
    public Page[] Pages { get; } = pages;
    public bool IsReadOnly { get; set; } = true;
    public string ProjectName { get; set; } = "Reference";
    public Connection[] Connections { get; set; } = [];
}

internal sealed class DMObjectsFinder(Project project) : IDisposable
{
    public Page[] GetPages(PagesFilter? filter) => filter is null ? project.Pages : [.. project.Pages.Where(p => p.Name.StartsWith(filter.Name, StringComparison.OrdinalIgnoreCase))];
    public Connection[] GetConnections(ConnectionsFilter? filter) => filter is null ? project.Connections
        : [.. project.Connections.Where(c => c.StartPin?.ParentFunction == filter.Function || c.EndPin?.ParentFunction == filter.Function)];
    public void Dispose() { }
}

internal sealed class PagesFilter : IDisposable
{
    public string Name { get; set; } = string.Empty;
    public bool ExactNameMatching { get; set; }
    public void Dispose() { }
}

internal sealed class AnyPropertyId(int id) : IDisposable
{
    public int AsInt { get; } = id;
    public short Index { get; set; }
    public PropertyDefinition Definition => new(AsInt);
    public static implicit operator AnyPropertyId(int id) => new(id);
    public void Dispose() { }
}

internal class Connection(long oid) : StorableObject(oid, 5)
{
    public Page? Page { get; set; }
    public Pin? StartPin { get; set; }
    public Pin? EndPin { get; set; }
}

internal sealed class Pin(Function? parent)
{
    public Function? ParentFunction => parent;
}

internal sealed class ConnectionsFilter : IDisposable
{
    public Function? Function { get; set; }
    public void Dispose() { }
}
