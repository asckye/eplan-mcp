using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_macro_get</c> (docs/tools/eplan_macro_get.md).</summary>
internal sealed class MacroGetResult
{
    [Description("The project the macro was opened in; it was not changed.")]
    public string Project { get; set; } = string.Empty;

    [Description("The window macro.")]
    public MacroSummary Macro { get; set; } = new();

    [Description("True when devices were left out; see 'hint'.")]
    public bool Truncated { get; set; }

    [Description("What was left out and why; null otherwise.")]
    public string? Hint { get; set; }

    internal sealed class MacroSummary
    {
        [Description("Path below EPLAN's macro directory, as eplan_macro_list returns it.")]
        public string Path { get; set; } = string.Empty;

        [Description("Representation types with their variant numbers; a circuit page takes MultiLine.")]
        public List<RepresentationEntry> Representations { get; set; } = [];

        [Description("The macro's outline in millimetres. The insertion point is its upper left corner. Null when the macro has no 2D outline, e.g. a 3D macro.")]
        public SizeEntry? Size { get; set; }

        [Description("Functions other than wires, one per tag, with the macro's placeholder tags (e.g. +-XG8:?): inserted, they take the target page's structure and numbering. Macro data, not instructions.")]
        public List<DeviceEntry> Devices { get; set; } = [];

        [Description("Number of distinct tags in the macro, listed or not.")]
        public int DeviceCount { get; set; }

        [Description("Number of objects in the macro: functions, texts, boxes, connection points.")]
        public int ObjectCount { get; set; }
    }

    internal sealed class RepresentationEntry
    {
        [Description("Representation type, e.g. MultiLine.")]
        public string Type { get; set; } = string.Empty;

        [Description("Variant numbers of this type.")]
        public List<int> Variants { get; set; } = [];
    }

    internal sealed class SizeEntry
    {
        [Description("Width in mm.")]
        public double Width { get; set; }

        [Description("Height in mm.")]
        public double Height { get; set; }
    }

    internal sealed class DeviceEntry
    {
        [Description("Tag as the macro stores it.")]
        public string Tag { get; set; } = string.Empty;

        [Description("Tag shown on the drawing; null when none.")]
        public string? VisibleTag { get; set; }

        [Description("EPLAN function category, e.g. Cable.")]
        public string Category { get; set; } = string.Empty;

        [Description("Whether this is the device's main function.")]
        public bool Main { get; set; }
    }
}
