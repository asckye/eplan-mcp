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
    public SymbolType Type { get; set; } = SymbolType.Function;
    public SymbolPropertyList StoredProperties { get; } = new();
    public int PropertyListReads { get; private set; }
    public SymbolPropertyList Properties { get { PropertyListReads++; return StoredProperties; } }
    public SymbolVariant[] Variants { get; set; } = [];
    public enum SymbolType { Function, BoxFunction, CableDefinitionLine }
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
    public string? Description { get; set; }
    public List<PropertyValue> Values { get; } = [];
    public List<Properties.Symbol> Reads { get; } = [];
    public int Disposals { get; private set; }
    public bool Exists(Properties.Symbol id) => id == Properties.Symbol.SYMB_DESC ? Description is not null : HasDefinition;
    public PropertyValue this[Properties.Symbol id]
    {
        get
        {
            Reads.Add(id);
            int disposals = Disposals;
            var value = new PropertyValue(id == Properties.Symbol.SYMB_DESC ? Description! : FormattableString.Invariant($"{(int)FunctionCategory.PLCTerminal}/1/1"), () => Assert.Equal(disposals, Disposals));
            Values.Add(value);
            return value;
        }
    }
    public void Dispose() => Disposals++;
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
