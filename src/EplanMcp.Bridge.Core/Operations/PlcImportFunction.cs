namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A function's object number, full name, category and PLC box identity, copied from EPLAN (S34, A39).</summary>
public sealed class PlcImportFunction(long id, string name, string category, bool isPlcBox)
{
    public long Id { get; } = id;

    public string Name { get; } = name;

    public string Category { get; } = category;

    public bool IsPlcBox { get; } = isPlcBox;
}
