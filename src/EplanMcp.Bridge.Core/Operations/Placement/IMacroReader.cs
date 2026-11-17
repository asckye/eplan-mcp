namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Where EPLAN keeps its window macros, and what one holds, implemented by the EPLAN adapter layer. Called on the main
/// thread only, inside the dispatcher's LockingStep. Scanning and every path check stay in Bridge.Core
/// (<see cref="MacroFiles"/>), where they can be tested without EPLAN.
/// </summary>
public interface IMacroReader
{
    /// <summary>
    /// The macro directory, <c>$(MD_MACROS)</c>, as EPLAN resolves it (api-notes A4); null when EPLAN cannot resolve it.
    /// </summary>
    public string? MacroRoot();

    /// <summary>
    /// Opens the window macro at <paramref name="macroPath"/>, a full path <see cref="MacroFiles.Resolve"/> checked, in
    /// the open project at <paramref name="projectPath"/>, without inserting it (S10: the project does not change), and
    /// copies out what it holds.
    /// </summary>
    /// <param name="projectPath">The resolved open project's full path.</param>
    /// <param name="macroPath">The path checked by MacroFiles.</param>
    /// <param name="defaultMultiLine">Select MultiLine variant 0 without caching when available; report the actual selection.</param>
    /// <param name="representation">Select this representation when available; null keeps the default selection.</param>
    /// <param name="variant">The variant within the selected representation.</param>
    public MacroContent ReadMacro(string projectPath, string macroPath, bool defaultMultiLine = false, string? representation = null, int variant = 0);

    /// <summary>Reads the configured parts database without modifying it (A82).</summary>
    public PartMacroSource ResolvePartMacro(string part, string? partVariant);
}

/// <summary>A selected part variant and its group symbol macro path relative to the macro directory.</summary>
public sealed class PartMacroSource
{
    public string Part { get; set; } = string.Empty;
    public string PartVariant { get; set; } = string.Empty;
    public string Macro { get; set; } = string.Empty;
}

/// <summary>What <see cref="IMacroReader.ReadMacro"/> copies out of a window macro.</summary>
public sealed class MacroContent
{
    public bool LayoutVerified { get; set; }
    /// <summary>True only when every function and object has been read without a limit.</summary>
    public bool Complete { get; set; }

    /// <summary>The representation actually read, not just one available in the file.</summary>
    public string Representation { get; set; } = string.Empty;

    /// <summary>The variant actually read.</summary>
    public int Variant { get; set; } = -1;

    /// <summary>The full function count before any reader limit.</summary>
    public int FunctionCount { get; set; }

    /// <summary>Each representation type's name with its variant numbers.</summary>
    public List<KeyValuePair<string, List<int>>> Representations { get; set; } = [];

    /// <summary>The outline's width, in millimetres.</summary>
    public double Width { get; set; }

    /// <summary>The outline's height, in millimetres.</summary>
    public double Height { get; set; }

    public double[]? ReferencePoint { get; set; }

    /// <summary>The lower left corner of the macro's outline, in the same coordinates as <see cref="ReferencePoint"/>.</summary>
    public double[]? Location { get; set; }

    /// <summary>Every function in the macro, in the macro's order.</summary>
    public List<MacroFunctionRecord> Functions { get; set; } = [];

    /// <summary>Readable placeholders of the selected representation and variant.</summary>
    public List<MacroPlaceholderRecord> Placeholders { get; set; } = [];

    /// <summary>How many objects the macro holds in all.</summary>
    public int ObjectCount { get; set; }

    /// <summary>Macro boxes among the selected representation's objects.</summary>
    public int MacroBoxCount { get; set; }
}

/// <summary>A placeholder's name, value set names and variables without reading its current values.</summary>
public sealed class MacroPlaceholderRecord
{
    public string Name { get; set; } = string.Empty;

    public List<string> ValueSets { get; set; } = [];

    public List<string> Variables { get; set; } = [];
}

/// <summary>A function in a macro, as plain strings.</summary>
public sealed class MacroFunctionRecord
{
    /// <summary>function, terminal, plc or cable, copied without retaining EPLAN objects.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary><c>Function.Name</c>, with the macro's structure placeholders.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Function.VisibleName</c>; empty when the drawing shows none.</summary>
    public string VisibleName { get; set; } = string.Empty;

    /// <summary>The <c>FunctionCategory</c> enum name.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary><c>Function.IsMainFunction</c>.</summary>
    public bool Main { get; set; }

    public double[]? Location { get; set; }
}
