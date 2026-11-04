using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel.Graphics;

namespace Eplan.EplApi.DataModel;

internal class StorableObject
{
    public bool IsValid { get; set; } = true;
    public bool IsReadOnly { get; set; }
    public Project? Project { get; set; }
}

internal interface IWriteProtection
{
    public bool WriteProtected { get; }
}

internal class Placement(long oid, int type = 4) : StorableObject, IDisposable
{
    private Page _page = null!;
    private bool? _isPlaced;
    public Page Page { get { CheckNative?.Invoke(); return _page; } set { CheckNative?.Invoke(); _page = value; } }
    public bool IsPlaced { get { CheckNative?.Invoke(); return _isPlaced ?? _page is not null; } set { CheckNative?.Invoke(); _isPlaced = value; } }
    public Action? CheckNative { get; set; }
    public long ObjectIdentifier { get { CheckNative?.Invoke(); return oid; } }
    public string Prefix { get; set; } = "53";
    public int IdentifierType { get; set; } = type;
    private PointD _location;
    public Exception? LocationError { get; set; }
    public PointD Location { get => LocationError is { } error ? throw error : _location; set => _location = value; }
    public virtual Pen Pen { get; set; }
    public Layer Layer { get; set; } = new() { Id = 0 };
    public int LayerId => Layer.Id;
    public Group? Group { get; set; }
    public PointD[] Bounds { get; set; } = [new(0, 0), new(10, 10)];
    public Exception? BoundsError { get; set; }
    public string ToStringIdentifier() => $"{Prefix}/{IdentifierType}/{ObjectIdentifier}/0";
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

internal class SymbolReference(long oid) : GraphicalPlacement(oid, 17)
{
    public BoxSymbolVariant SymbolVariant { get; } = new();
    public PinBase[] GraphicalConnectionPoints { get; set; } = [];
    public GraphicalConnection[] GraphicalConnections { get; set; } = [];
    public PropertyPlacement[] PropertyPlacements { get; set; } = [];
    public bool UseLocalPropertyPlacements { get; set; } = true;
    public int LocalCopies { get; private set; }
    public Action? BeforeLocalCopy { get; set; }
    public void SwitchLocalPropertyPlacements(bool useLocal)
    {
        Assert.True(useLocal);
        Assert.False(UseLocalPropertyPlacements);
        BeforeLocalCopy?.Invoke();
        PropertyPlacements = [.. PropertyPlacements.Select(p => new ContextPropertyPlacement(ObjectIdentifier, this)
        {
            IdentifierType = 17,
            Location = p.Location, Bounds = p is ContextPropertyPlacement ? p.Bounds : p.Bounds.Select(b => new PointD(b.X - Location.X, b.Y - Location.Y)).ToArray(), DisplayedId = p.DisplayedId,
            Height = p.Height, Justification = p.Justification, Pen = p.Pen, Layer = p.Layer, IsVisible = p.IsVisible,
            Display = p.Display, Contents = new(p.Contents.GetAsString()), Rotation = p.Rotation, IsDocked = p.IsDocked,
        })];
        UseLocalPropertyPlacements = true;
        LocalCopies++;
    }
    public static SymbolReference Create(object variant, Page page) => LocationBox.CreateBox(variant, page);
}

internal sealed class DynamicConnectionLine(long oid) : SymbolReference(oid);

internal sealed class PinBase
{
    public int Index { get; set; }
    public PointD Location { get; set; }
    public string Direction { get; set; } = "Right";
}

internal class FunctionBase(long oid) : SymbolReference(oid)
{
    private string _name = "+A1-K1";
    public int NameReads { get; private set; }
    public int NameWrites { get; private set; }
    public string Name
    {
        get { CheckNative?.Invoke(); NameReads++; return IsValid ? _name : throw new BaseException("S063001错误:无对象"); }
        set { CheckNative?.Invoke(); NameWrites++; _name = value; }
    }
}

internal partial class Function(long oid = 1) : FunctionBase(oid)
{
    private bool _main;
    public bool IsMainFunction { get { CheckNative?.Invoke(); return _main; } set { CheckNative?.Invoke(); _main = value; } }
    private readonly FunctionPropertyList _properties = new();
    public FunctionPropertyList Properties { get { CheckNative?.Invoke(); return _properties; } }
    private MasterData.FunctionDefinition _definition = new();
    public MasterData.FunctionDefinition FunctionDefinition
    {
        get => _definition;
        set { _definition = value; FunctionCategory = value.FunctionCategory; }
    }
    public void Create(Page page, MasterData.SymbolVariant variant)
    {
        Page = page;
        UseLocalPropertyPlacements = false;
        SymbolVariant.Library = variant.SymbolLibraryName;
        SymbolVariant.Name = variant.SymbolName;
        SymbolVariant.Variant = variant.VariantNr;
        page.AllPlacements = [.. page.AllPlacements, this];
        if (this is EObjects.Terminal terminal)
        { EObjects.Terminal.OnCreate?.Invoke(terminal); }
    }
    public string? VisibleName { get; set; }
    public FunctionCategory FunctionCategory { get; set; } = FunctionCategory.Terminal;
    private ArticleReference[] _articleReferences = [];
    public ArticleReference[] ArticleReferences { get { CheckNative?.Invoke(); return _articleReferences; } set { CheckNative?.Invoke(); _articleReferences = value; } }
    public Function[] SubFunctions { get; set; } = [];
    public Function? ParentFunction { get; set; }
    public Pin[] Pins { get; set; } = [];
}

internal sealed class PlotFrame(params Placement[] children)
{
    public Placement[] SubPlacements { get; } = children;
    public PointD Size { get; set; } = new(420, 297);
}

internal sealed class Page : StorableObject, IWriteProtection, IDisposable
{
    public ReportBlock? ReportBlock { get; set; }
    public bool WriteProtected { get; set; }
    public DocumentTypeManager.DocumentType PageType { get; set; } = DocumentTypeManager.DocumentType.Circuit;
    public string Name { get; set; } = "+A1/1";
    public double GridSize { get; set; } = 4;
    public PointD StoredSize { get; set; } = new(420, 297);
    public Exception? SizeError { get; set; }
    public PointD Size => SizeError is { } error ? throw error : StoredSize;
    private PlotFrame? _frame;
    public Exception? FrameError { get; set; }
    public PlotFrame? PlotFrame { get => FrameError is { } error ? throw error : _frame; set => _frame = value; }
    private Placement[] _placements = [];
    public Func<Placement[]>? FindPlacements { get; set; }
    public Placement[] AllPlacements { get => FindPlacements?.Invoke() ?? _placements; set => _placements = value; }
    public Action? AfterGraphicCreate { get; set; }
    public void Dispose() => throw new InvalidOperationException("Borrowed page must never be disposed.");
}

internal sealed class Layer { public int Id { get; set; } }
internal sealed class LayerTable { public Layer[] Layers { get; set; } = [new() { Id = 0 }, new() { Id = 3 }]; }

internal sealed class Project(params Page[] pages) : StorableObject
{
    public Action<int, string, FunctionPropertyList, FunctionPropertyList>? ParseName { get; set; }
    public void ParseNameAndProps(int category, string name, FunctionPropertyList parsed, FunctionPropertyList additional) => ParseName!(category, name, parsed, additional);
    public bool Locked { get; private set; }
    public void LockAllObjects() { EplanMcp.Bridge.Core.Tests.Adapters.FakeMoveNative.EndTransaction = EndTransaction; Locked = true; EplanMcp.Bridge.Core.Tests.Adapters.FakeMoveNative.Calls.Add("LockAll"); }
    public Action? EndTransaction { get; set; }
    public string ProjectFullName { get; set; } = "project";
    public Page[] Pages { get; } = pages;
    public LayerTable LayerTable { get; } = new();
    public string Display { get; set; } = "zh_CN;";
    public Function[] UnplacedFunctions { get; set; } = [];
    public bool RejectUnfilteredFunctions { get; set; }
    public bool OmitTerminalsFromFunctions { get; set; }
    public Func<FunctionsFilter?, Function[]>? FindFunctions { get; set; }
    public List<(string? Name, bool Exact, FunctionCategory? Category)> FunctionQueries { get; } = [];
    public MasterData.SymbolLibrary[] SymbolLibraries { get; set; } =
        [new() { Name = "SPECIAL", Symbols = [new() { Type = MasterData.Symbol.SymbolType.CableDefinitionLine, Variants = [new()] }, new() { Name = "TDEF", Variants = [new() { SymbolLibraryName = "SPECIAL", SymbolName = "TDEF" }] }] }];
    public MasterData.FunctionDefinitionLibrary FunctionDefinitionLibrary { get; } = new();
    public Func<Connection[]>? FindConnections { get; set; }
}

internal static class DocumentTypeManager
{
    public enum DocumentType { Circuit, Graphics, TableOfContents, Overview }
}

internal sealed class DMObjectsFinder(Project project) : IDisposable
{
    public EObjects.TerminalStrip[] GetTerminalStrips(object? filter) => [.. project.Pages.SelectMany(p => p.AllPlacements).OfType<EObjects.TerminalStrip>()];
    public Function[] GetFunctions(FunctionsFilter? filter)
    {
        Assert.False(project.RejectUnfilteredFunctions && filter is null, "A whole-project function scan is forbidden for this read.");
        project.FunctionQueries.Add((filter?.Name, filter?.ExactNameMatching ?? false, filter?.FunctionCategory));
        if (project.FindFunctions is { } find)
        { return find(filter); }
        IEnumerable<Function> functions = project.Pages.SelectMany(p => p.AllPlacements).OfType<Function>().Concat(project.UnplacedFunctions);
        return [.. functions.Where(f => (filter?.Name is not { } name || (filter.ExactNameMatching ? f.Name == name : f.Name.StartsWith(name, StringComparison.Ordinal)))
            && (filter?.FunctionCategory is not { } category || f.FunctionCategory == category)
            && (filter?.Page is null || f.Page == filter.Page)
            && !(project.OmitTerminalsFromFunctions && f is EObjects.Terminal))];
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
