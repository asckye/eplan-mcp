namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ShieldCreatePlan"/> and <see cref="OperationNames.ShieldCreateExecute"/> (docs/tools/eplan_shield_create.md).</summary>
public sealed class ShieldCreateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full page name, found as eplan_page_get finds a page.</summary>
    public string? Page { get; set; }

    /// <summary>The start corner of the shield's area; EPLAN puts the shield's connection point on this side.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>The corner opposite the start.</summary>
    public double CornerX { get; set; }

    public double CornerY { get; set; }

    /// <summary>The shield's tag; null leaves it unnamed, which EPLAN allows.</summary>
    public string? Name { get; set; }

    /// <summary>The symbol's name in the SPECIAL library; null is <c>SH</c>.</summary>
    public string? Symbol { get; set; }

    /// <summary>A point on each wire inside the area, where its connection definition point goes; a shield covers only wires that have one (api-notes A112).</summary>
    public double[][]? WirePoints { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>The objects one eplan_shield_create call created.</summary>
public sealed class ShieldObjects
{
    public long Shield { get; set; }

    public List<long> DefinitionPoints { get; set; } = [];
}

/// <summary>Data of <see cref="OperationNames.ShieldCreateExecute"/>.</summary>
public sealed class ShieldCreateData
{
    public string Project { get; set; } = string.Empty;

    public string Page { get; set; } = string.Empty;

    /// <summary>The shield's tag as EPLAN reports it.</summary>
    public string Name { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    /// <summary>The symbol variant EPLAN chose from the start corner.</summary>
    public int Variant { get; set; }

    /// <summary>The area, <c>[minX, minY, maxX, maxY]</c>.</summary>
    public double[] Bounds { get; set; } = [];

    /// <summary>The connections the shield covers after generation, <c>from -> to</c>.</summary>
    public List<string> Connections { get; set; } = [];

    public string? Warning { get; set; }
}
