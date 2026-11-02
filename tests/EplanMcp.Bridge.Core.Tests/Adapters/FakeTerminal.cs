namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class TerminalStrip() : Function(2)
{
    public Terminal[] Terminals { get; set; } = [];
    public new TerminalStripPropertyListComplete Properties { get; } = new();
    public void Create(Page page) { Page = page; IsPlaced = true; page.AllPlacements = [.. page.AllPlacements, this]; }
    // Unplaced definition: navigator only, never a page placement.
    public Project? DefinedIn { get; private set; }
    public void Create(Project project, MasterData.FunctionDefinition definition)
    {
        Assert.Equal(Eplan.EplApi.Base.Enums.FunctionCategory.TerminalDefText, definition.FunctionCategory);
        DefinedIn = project;
    }
}

internal sealed class Terminal(long oid = 3) : Function(oid)
{
    [ThreadStatic]
    public static Action<Terminal>? OnCreate;
    private TerminalStrip? _owner;
    public TerminalStrip? TerminalStrip => _owner;
    public PLC? PLC { get; set; }
    public void Create(TerminalStrip strip, Page page, object variant)
    {
        _owner = strip;
        Page = page;
        var symbol = (MasterData.SymbolVariant)variant;
        SymbolVariant.Library = symbol.SymbolLibraryName;
        SymbolVariant.Name = symbol.SymbolName;
        SymbolVariant.Variant = symbol.VariantNr;
        page.AllPlacements = [.. page.AllPlacements, this];
        OnCreate?.Invoke(this);
    }
    public void ClearOwner() => _owner = null;
    public void ForeignOwner() => _owner = new TerminalStrip { Name = "+A1-XOTHER" };
}
