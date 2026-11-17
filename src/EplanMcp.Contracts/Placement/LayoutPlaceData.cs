namespace EplanMcp.Contracts;

/// <summary>Data of <see cref="OperationNames.LayoutPlaceExecute"/>: the items as read back after the commit.</summary>
public sealed class LayoutPlaceData
{
    /// <summary>The resolved project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The installation space designation.</summary>
    public string Space { get; set; } = string.Empty;

    /// <summary>The placed items, in the order asked.</summary>
    public List<LayoutPlaced> Items { get; set; } = [];

    /// <summary>Items whose boxes overlap others on the same surface; the placement was kept (ADR-0046).</summary>
    public List<string> Overlaps { get; set; } = [];

    /// <summary>A problem after the commit that did not undo it; null when none.</summary>
    public string? Warning { get; set; }
}

/// <summary>A placed item as read back.</summary>
public sealed class LayoutPlaced
{
    /// <summary>Its index in the request.</summary>
    public int Index { get; set; }

    /// <summary><c>rail</c>, <c>duct</c> or <c>device</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The object ID in this session.</summary>
    public long Id { get; set; }

    /// <summary>The device tag; null for rails and ducts.</summary>
    public string? Tag { get; set; }

    /// <summary>The part number placed.</summary>
    public string Part { get; set; } = string.Empty;

    /// <summary>Box in the space: min x, y, z, then size x, y, z (millimetres).</summary>
    public double[] Box { get; set; } = [];

    /// <summary>Lower left corner on its surface (x, y), millimetres, as measured after placing.</summary>
    public double[] At { get; set; } = [];
}
