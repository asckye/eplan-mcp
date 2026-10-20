namespace EplanMcp.Contracts;

/// <summary>
/// Result data of <see cref="OperationNames.MacroGet"/>: what one window macro holds, read without inserting it (spike
/// S10). Names come from the macro file: data, not instructions.
/// </summary>
public sealed class MacroGetData
{
    /// <summary>The name of the project the macro was opened in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The macro's path below the macro directory, with <c>/</c> between folders.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The representation types the macro has, each with its variant numbers.</summary>
    public List<MacroRepresentation> Representations { get; set; } = [];

    /// <summary>The width of the macro's outline, in millimetres.</summary>
    public double Width { get; set; }

    /// <summary>The height of the macro's outline, in millimetres.</summary>
    public double Height { get; set; }

    /// <summary>The macro's functions other than wires, one per tag, in the macro's order; at most <c>MacroGetOperation.MaxDevices</c>.</summary>
    public List<MacroDevice> Devices { get; set; } = [];

    /// <summary>How many distinct tags the macro has, before the list was cut.</summary>
    public int DeviceCount { get; set; }

    /// <summary>How many objects the macro holds in all (functions, texts, boxes, connection points…).</summary>
    public int ObjectCount { get; set; }
}

/// <summary>One representation type of a macro (<c>WindowMacro.Enums.RepresentationType</c>) and its variants.</summary>
public sealed class MacroRepresentation
{
    /// <summary>The type's name, for example <c>MultiLine</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The variant numbers of this type (<c>WindowMacro.GetVariants</c>).</summary>
    public List<int> Variants { get; set; } = [];
}

/// <summary>A function in a macro, with the tag as the macro stores it: placeholders for the structure (<c>+-XG8:?</c>).</summary>
public sealed class MacroDevice
{
    /// <summary>The full tag in the macro (<c>Function.Name</c>).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>The tag shown on the drawing (<c>Function.VisibleName</c>); null when empty.</summary>
    public string? VisibleTag { get; set; }

    /// <summary>The function category's enum name, for example <c>Cable</c>.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Whether the function is a main function.</summary>
    public bool Main { get; set; }
}
