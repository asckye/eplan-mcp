using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel;

internal sealed class UniversalPropertyList : IDisposable
{
    public Dictionary<(int Id, int Index), object?> Values { get; } = [];
    public Exception? ReadError { get; set; }
    public int FailAfterReads { get; set; } = int.MaxValue;
    public int Reads { get; private set; }
    public int Disposals { get; private set; }
    public AnyPropertyId[] ExistingIds => [.. Values.Keys.Select(k => k.Id).Distinct().Select(id => new AnyPropertyId(id))];
    public PropertyValue this[AnyPropertyId id] => Read(id.AsInt, id.Index);
    public PropertyValue this[AnyPropertyId id, int index] => Read(id.AsInt, index);
    private PropertyValue Read(int id, int index)
    {
        if (++Reads > FailAfterReads)
        { throw new BaseException(); }
        if (ReadError is { } error)
        { throw error; }
        return new(id, Values.GetValueOrDefault((id, index)), [.. Values.Keys.Where(k => k.Id == id).Select(k => k.Index)]);
    }
    public void Dispose()
    {
        Assert.Equal(0, Disposals++);
    }
}

internal sealed class PropertyDefinition(int id) : IDisposable
{
    public enum PropertyType { Bool, String, MultilangString }
    public PropertyType Type => id == 20093 ? PropertyType.Bool : id == 20011 ? PropertyType.MultilangString : PropertyType.String;
    public bool IsIndexed => id == 20100;
    public void Dispose() { }
}

internal sealed class PropertyValue(int id, object? value, int[] indexes) : IDisposable
{
    public PropertyDefinition Definition => new(id);
    public bool IsEmpty => value is null;
    public int[] Indexes => indexes;
    public bool ToBool() => (bool)value!;
    public MultiLangString ToMultiLangString() => new((string)value!);
    public override string ToString() => value?.ToString() ?? throw new InvalidOperationException("Empty property was formatted.");
    public void Dispose() { }
}
