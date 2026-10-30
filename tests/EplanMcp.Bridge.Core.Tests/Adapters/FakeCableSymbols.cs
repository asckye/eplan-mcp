namespace Eplan.EplApi.DataModel.MasterData;

internal sealed class SymbolLibrary
{
    public string Name { get; } = "SPECIAL";
    public Symbol[] Symbols { get; } = [new()];
}

internal sealed class Symbol
{
    public SymbolType Type { get; } = SymbolType.CableDefinitionLine;
    public SymbolVariant[] Variants { get; } = [new()];
    public enum SymbolType { CableDefinitionLine }
}

internal sealed class SymbolVariant
{
    public int VariantNr { get; }
}
