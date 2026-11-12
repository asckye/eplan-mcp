namespace EplanMcp.Contracts;

/// <summary>Data of <see cref="OperationNames.LayoutGet"/>: millimetres, X across, Y depth (front small), Z up (A136).</summary>
public sealed class LayoutGetData
{
    /// <summary>The resolved project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Every installation space of the project, or the requested one.</summary>
    public List<LayoutSpace> Spaces { get; set; } = [];

    /// <summary>Placed objects of these spaces on this page, enclosures first, then in EPLAN's order.</summary>
    public List<LayoutObject> Objects { get; set; } = [];

    /// <summary>Number of placed objects in total.</summary>
    public int Total { get; set; }

    /// <summary>Schematic devices with a part and no 3D representation yet, by tag; at most 200.</summary>
    public List<LayoutDevice> Unplaced { get; set; } = [];

    /// <summary>Number of such devices in total.</summary>
    public int UnplacedTotal { get; set; }
}

/// <summary>An installation space with its enclosures.</summary>
public sealed class LayoutSpace
{
    /// <summary>The space's designation (36452), as layout tools and InsertModelViewAction take it (A136).</summary>
    public string Designation { get; set; } = string.Empty;

    /// <summary>The space's name as EPLAN shows it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The enclosures placed in the space.</summary>
    public List<LayoutEnclosure> Enclosures { get; set; } = [];
}

/// <summary>An enclosure with the surfaces devices go on.</summary>
public sealed class LayoutEnclosure
{
    /// <summary>The enclosure's object ID in this session.</summary>
    public long Id { get; set; }

    /// <summary>Part numbers of the enclosure.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary>Outer box: min x, y, z, then size x, y, z.</summary>
    public double[] Box { get; set; } = [];

    /// <summary>The mounting panel's front surface, or null when the enclosure has none.</summary>
    public LayoutSurface? Panel { get; set; }

    /// <summary>The door's inner mounting surface, or null when the enclosure has none.</summary>
    public LayoutSurface? Door { get; set; }
}

/// <summary>A surface devices are mounted on: width and height in its own plane, and its box in the space.</summary>
public sealed class LayoutSurface
{
    /// <summary>The surface owner's object ID in this session.</summary>
    public long Id { get; set; }

    /// <summary>Width across, millimetres.</summary>
    public double Width { get; set; }

    /// <summary>Height, millimetres.</summary>
    public double Height { get; set; }

    /// <summary>Box in the space: min x, y, z, then size x, y, z.</summary>
    public double[] Box { get; set; } = [];
}

/// <summary>A placed 3D object.</summary>
public sealed class LayoutObject
{
    /// <summary>The installation space it belongs to (designation).</summary>
    public string Space { get; set; } = string.Empty;

    /// <summary>The object's object ID in this session.</summary>
    public long Id { get; set; }

    /// <summary>EPLAN's type: Cabinet, MountingPanel, MountingRail, Duct, Component and so on.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The device tag of a component, null for parts of the enclosure.</summary>
    public string? Tag { get; set; }

    /// <summary>Part numbers.</summary>
    public List<string> Parts { get; set; } = [];

    /// <summary>The parent's type (Plane, MountingRail, InstallationSpace ...).</summary>
    public string? Parent { get; set; }

    /// <summary>The parent's object ID, when the parent is a placement.</summary>
    public long? ParentId { get; set; }

    /// <summary>Box in the space: min x, y, z, then size x, y, z.</summary>
    public double[] Box { get; set; } = [];
}

/// <summary>A schematic device that has a part and no 3D representation.</summary>
public sealed class LayoutDevice
{
    /// <summary>The device tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Its first part number.</summary>
    public string Part { get; set; } = string.Empty;
}
