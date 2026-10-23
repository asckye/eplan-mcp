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
    public MacroContent ReadMacro(string projectPath, string macroPath, bool defaultMultiLine = false);
}

/// <summary>What <see cref="IMacroReader.ReadMacro"/> copies out of a window macro.</summary>
public sealed class MacroContent
{
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

    /// <summary>Every function in the macro, in the macro's order.</summary>
    public List<MacroFunctionRecord> Functions { get; set; } = [];

    /// <summary>How many objects the macro holds in all.</summary>
    public int ObjectCount { get; set; }
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
}
