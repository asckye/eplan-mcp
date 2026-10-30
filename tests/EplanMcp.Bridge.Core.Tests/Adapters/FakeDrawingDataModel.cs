using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel.Graphics;

namespace Eplan.EplApi.DataModel;

internal class Placement(long oid, int type = 4) : IDisposable
{
    public long ObjectIdentifier { get; } = oid;
    public string Prefix { get; set; } = "53";
    public PointD Location { get; set; }
    public Group? Group { get; set; }
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
    public static SymbolReference Create(object variant, Page page) => LocationBox.CreateBox(variant, page);
}

internal class FunctionBase(long oid) : SymbolReference(oid)
{
    public string Name { get; set; } = "+A1-K1";
}

internal class Function(long oid) : FunctionBase(oid)
{
    public Page Page { get; set; } = null!;
    public bool IsMainFunction { get; set; }
    public string? VisibleName { get; set; }
    public FunctionCategory FunctionCategory { get; set; } = FunctionCategory.Terminal;
    public ArticleReference[] ArticleReferences { get; set; } = [];
    public Function[] SubFunctions { get; set; } = [];
    public Pin[] Pins { get; set; } = [];
}

internal sealed class PlotFrame(params Placement[] children)
{
    public Placement[] SubPlacements { get; } = children;
    public PointD Size { get; set; } = new(420, 297);
}

internal sealed class Page : IDisposable
{
    public DocumentTypeManager.DocumentType PageType { get; set; } = DocumentTypeManager.DocumentType.Circuit;
    public string Name { get; set; } = "+A1/1";
    public double GridSize { get; set; } = 4;
    public PointD StoredSize { get; set; } = new(420, 297);
    public Exception? SizeError { get; set; }
    public PointD Size => SizeError is { } error ? throw error : StoredSize;
    public PlotFrame? PlotFrame { get; set; }
    private Placement[] _placements = [];
    public Func<Placement[]>? FindPlacements { get; set; }
    public Placement[] AllPlacements { get => FindPlacements?.Invoke() ?? _placements; set => _placements = value; }
    public Action? AfterGraphicCreate { get; set; }
    public void Dispose() => throw new InvalidOperationException("Borrowed page must never be disposed.");
}

internal sealed class Project(params Page[] pages)
{
    public Page[] Pages { get; } = pages;
    public string Display { get; set; } = "zh_CN;";
    public Function[] UnplacedFunctions { get; set; } = [];
    public bool RejectUnfilteredFunctions { get; set; }
    public List<(string? Name, bool Exact, FunctionCategory? Category)> FunctionQueries { get; } = [];
    public MasterData.SymbolLibrary[] SymbolLibraries { get; set; } = [];
    public MasterData.FunctionDefinitionLibrary FunctionDefinitionLibrary { get; } = new();
    public Func<Connection[]>? FindConnections { get; set; }
}

internal static class DocumentTypeManager
{
    public enum DocumentType { Circuit, Graphics }
}

internal sealed class DMObjectsFinder(Project project) : IDisposable
{
    public EObjects.TerminalStrip[] GetTerminalStrips(object? filter) => [.. project.Pages.SelectMany(p => p.AllPlacements).OfType<EObjects.TerminalStrip>()];
    public Function[] GetFunctions(FunctionsFilter? filter)
    {
        Assert.False(project.RejectUnfilteredFunctions && filter is null, "A whole-project function scan is forbidden for this read.");
        project.FunctionQueries.Add((filter?.Name, filter?.ExactNameMatching ?? false, filter?.FunctionCategory));
        IEnumerable<Function> functions = project.Pages.SelectMany(p => p.AllPlacements).OfType<Function>().Concat(project.UnplacedFunctions);
        return [.. functions.Where(f => (filter?.Name is not { } name || (filter.ExactNameMatching ? f.Name == name : f.Name.StartsWith(name, StringComparison.Ordinal)))
            && (filter?.FunctionCategory is not { } category || f.FunctionCategory == category))];
    }
    public Connection[] GetConnections(object? filter) => project.FindConnections?.Invoke() ?? [];
    public Page[] GetPages(PagesFilter filter) => [.. project.Pages.Where(p => p.Name.StartsWith(filter.Name, StringComparison.OrdinalIgnoreCase))];
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
    public static implicit operator AnyPropertyId(int id) => new(id);
    public void Dispose() { }
}
