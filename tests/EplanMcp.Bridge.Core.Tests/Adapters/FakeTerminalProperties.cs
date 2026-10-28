namespace Eplan.EplApi.DataModel;

internal sealed class FunctionPropertyList : IDisposable
{
    public PropertyValue FUNC_PINORTERMINALNUMBER { get; set; } = new("");
    public PropertyValue FUNC_TERMINALSORTCODE { get; set; } = new("17");
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class TerminalStripPropertyListComplete : IDisposable
{
    public PropertyValue FUNC_TEXT { get; set; } = new("");
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}
