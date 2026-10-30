namespace Eplan.EplApi.DataModel;

internal sealed class FunctionPropertyList : IDisposable
{
    public PropertyValue FUNC_PINORTERMINALNUMBER { get; set; } = new("");
    public PropertyValue FUNC_TERMINALSORTCODE { get; set; } = new("17");
    public PropertyValue FUNC_PLCSTATIONNAME { get; set; } = new("");
    public PropertyValue FUNC_PLCRACK { get; set; } = new("");
    public PropertyValue FUNC_PLCSOCKET { get; set; } = new("");
    public PropertyValue FUNC_TEXT { get; set; } = new("");
    public PropertyValue FUNC_PLCPAIRCROSSREFERENCE { get; set; } = new("");
    public PropertyValue FUNC_PLCSYMBOLICADDRESS_MANUAL { get; set; } = new("");
    public PropertyValue FUNC_PLCADDRESS { get; set; } = new("");
    public PropertyValue FUNC_PLCSYMBOLICADDRESS_CALCULATED { get; set; } = new("");
    public PropertyValue FUNC_PLCDATATYPE { get; set; } = new("");
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class TerminalStripPropertyListComplete : IDisposable
{
    public PropertyValue FUNC_TEXT { get; set; } = new("");
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}
