namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class TerminalStrip() : Function(2)
{
    public Terminal[] Terminals { get; set; } = [];
    public TerminalStripPropertyListComplete Properties { get; } = new();
    public void Create(Page page) => page.AllPlacements = [.. page.AllPlacements, this];
}

internal sealed class Terminal() : Function(3)
{
    [ThreadStatic]
    public static Action<Terminal>? OnCreate;
    private TerminalStrip? _owner;
    public TerminalStrip? TerminalStrip => _owner;
    public FunctionPropertyList Properties { get; } = new();
    public bool IsPlaced { get; set; } = true;
    public PLC? PLC { get; set; }
    public BoxSymbolVariant SymbolVariant { get; } = new() { Library = "IEC_symbol", Name = "X" };
    public void Create(TerminalStrip strip, Page page, object variant)
    {
        _owner = strip;
        Page = page;
        page.AllPlacements = [.. page.AllPlacements, this];
        OnCreate?.Invoke(this);
    }
    public void ForeignOwner() => _owner = new TerminalStrip { Name = "+A1-XOTHER" };
}
