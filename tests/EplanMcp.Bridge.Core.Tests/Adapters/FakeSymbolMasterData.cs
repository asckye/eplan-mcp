using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;

namespace Eplan.EplApi.DataModel.MasterData;

internal sealed class SymbolLibrary
{
    public string Name { get; set; } = "IEC_symbol";
    public Symbol[] Symbols { get; set; } = [];
}

internal sealed class Symbol
{
    public string Name { get; set; } = "PLC_DI";
    public string Type { get; set; } = "Function";
    public SymbolPropertyList Properties { get; } = new();
    public SymbolVariant[] Variants { get; set; } = [];
}

internal sealed class SymbolVariant
{
    public int VariantNr { get; set; }
    public NativeSymbolPoint[] ConnectionPoints { get; set; } = [];
}

internal sealed class NativeSymbolPoint
{
    public PointD Location { get; set; } = new(1, 2);
    public string Direction { get; set; } = "Up";
}

internal sealed class SymbolPropertyList : IDisposable
{
    public bool HasDefinition { get; set; } = true;
    public bool Exists(Properties.Symbol id) => HasDefinition;
    public PropertyValue this[Properties.Symbol id] => new(FormattableString.Invariant($"{(int)FunctionCategory.PLCTerminal}/1/1"));
    public void Dispose() { }
}

internal sealed class FunctionDefinitionLibrary
{
    public FunctionDefinition[] Definitions { get; set; } = [];
    public int Reads { get; private set; }
    public FunctionDefinition[] FunctionDefinitions { get { Reads++; return Definitions; } }
}

internal sealed class FunctionDefinition
{
    public FunctionCategory FunctionCategory { get; set; } = FunctionCategory.PLCTerminal;
    public NativeSymbolPoint[] ConnectionPoints { get; set; } = [];
    public int Group { get; set; } = 1;
    public int Id { get; set; } = 2;
    public string Text { get; set; } = "Digital input";
    public string GroupText { get; set; } = "PLC";
    public MultiLangString Name => new("en_US@" + Text + ";");
    public MultiLangString GroupName => new("en_US@" + GroupText + ";");
}
