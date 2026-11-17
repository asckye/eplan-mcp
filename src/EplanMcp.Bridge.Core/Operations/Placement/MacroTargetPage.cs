using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The page a macro goes on, as <see cref="IMacroInserter.ReadPage"/> reads it.</summary>
public sealed class MacroTargetPage
{
    public LayoutGuard.Model? Layout { get; set; }
    public Func<DevicePlaceArgs, IReadOnlyList<DrawingPlacement>>? PredictDevice { get; set; }
    /// <summary>Existing boxes on this page as <see cref="BoxKey"/>: only an exact repeat (same identifier and area) conflicts (A55).</summary>
    public List<string> LocationBoxStructures { get; set; } = [];

    /// <summary>A location box's identifier and logical area; several boxes may share an identifier on one page.</summary>
    public static string BoxKey(string structure, double x0, double y0, double x1, double y1) =>
        FormattableString.Invariant($"{structure}|{x0},{y0},{x1},{y1}");

    /// <summary>The page's full name as EPLAN has it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>DocumentType</c> enum name, for example <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The page's width in millimetres (<c>Page.Size</c>); null when EPLAN cannot tell it, as on a new, empty page (A14).</summary>
    public double? Width { get; set; }

    /// <summary>The page's height in millimetres; null together with <see cref="Width"/>.</summary>
    public double? Height { get; set; }

    /// <summary>The session identifier (<c>ToStringIdentifier</c>, valid in this EPLAN session only, A3) of every object on the page.</summary>
    public List<string> Objects { get; set; } = [];
}
