namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One registered property value as EPLAN gave it: <c>PropertyValue.ToString()</c> of a value that is not empty.</summary>
public sealed class SnapshotSourceValue
{
    public SnapshotSourceValue(int id, int? index, string raw)
    {
        Id = id;
        Index = index;
        Raw = raw;
    }

    /// <summary>EPLAN's property number.</summary>
    public int Id { get; }

    /// <summary>The index of an indexed property's value; null otherwise.</summary>
    public int? Index { get; }

    /// <summary>The raw text; a multi-language value is <c>de_DE@…;en_US@…;</c> (api-notes A11).</summary>
    public string Raw { get; }
}
