namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PlcGeneratePlan"/> and <see cref="OperationNames.PlcGenerateExecute"/> (docs/tools/eplan_plc_generate.md).</summary>
public sealed class PlcGenerateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The PLC devices to draw, with the new page of each representation.</summary>
    public List<PlcGenerateDevice>? Devices { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>One PLC device of <see cref="PlcGenerateArgs"/>; a page that is given selects its representation.</summary>
public sealed class PlcGenerateDevice
{
    /// <summary>The device's full tag.</summary>
    public string? Tag { get; set; }

    /// <summary>A macro below the macro directory, or null for the one EPLAN knows from the device's part.</summary>
    public string? Macro { get; set; }

    public string? SingleLinePage { get; set; }

    public string? MultiLinePage { get; set; }

    public string? OverviewPage { get; set; }

    public string? RackPage { get; set; }
}

/// <summary>Data of <see cref="OperationNames.PlcGenerateExecute"/>.</summary>
public sealed class PlcGenerateData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The pages EPLAN generated.</summary>
    public List<PlcGeneratedPage> Pages { get; set; } = [];

    public string? Warning { get; set; }
}

/// <summary>A page <c>PlcService.GenerateSchematic</c> generated.</summary>
public sealed class PlcGeneratedPage
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    /// <summary>The functions placed on it, at most 200 listed.</summary>
    public List<string> Functions { get; set; } = [];

    public int FunctionCount { get; set; }
}

/// <summary>What the project's PLC schematic configuration knows of a device.</summary>
public sealed class PlcSchematicDevice
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The macro of the device's main function as EPLAN wrote it; null when it has none.</summary>
    public string? Macro { get; set; }
}
