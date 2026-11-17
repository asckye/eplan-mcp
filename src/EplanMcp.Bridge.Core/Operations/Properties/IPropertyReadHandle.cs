namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns the ID and any parent value; null acquisitions mean not applicable, never an empty value.</summary>
public interface IPropertyReadHandle : IDisposable
{
    public IPropertyReadDefinition OpenDefinition();
    /// <summary>Copy sorted existing indexes only, at most count (1..200); a short block ends enumeration.</summary>
    public IReadOnlyList<int> ReadIndexes(int offset, int count);
    public IPropertyReadValue? OpenValue(int? index);
}
