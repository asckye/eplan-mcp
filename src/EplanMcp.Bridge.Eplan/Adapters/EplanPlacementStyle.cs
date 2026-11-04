using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanPlacementStyle
{
    internal static void Validate(Project project, int? layer, string? alignment)
    {
        if (layer is { } id && !project.LayerTable.Layers.Any(l => l.Id == id))
        { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Unknown layer ID."); }
        if (alignment is not null && (!Enum.TryParse(alignment, out TextBase.JustificationType value) || !Enum.IsDefined(typeof(TextBase.JustificationType), value) || value.ToString() != alignment))
        { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Unknown alignment name."); }
    }
    internal static void Apply(GraphicalPlacement placement, Project project, int? layer, IReadOnlyDictionary<string, double>? values)
    {
        if (layer is { } id)
        { placement.Layer = project.LayerTable.Layers.First(l => l.Id == id); }
        if (values is null)
        { return; }
        Pen pen = placement.Pen;
        foreach (var pair in values)
        {
            switch (pair.Key)
            {
                case "color":
                    pen.ColorId = checked((short)pair.Value);
                    break;
                case "width_mm":
                    pen.Width = pair.Value;
                    break;
                case "style":
                    pen.StyleId = checked((short)pair.Value);
                    break;
                case "pattern_length":
                    pen.PatternLength = pair.Value;
                    break;
                case "line_end":
                    pen.LineEndType = checked((sbyte)pair.Value);
                    break;
            }
        }
        placement.Pen = pen;
    }
    internal static PlacementText Read(GraphicalPlacement placement, PlacementText requested, PlacementText actual)
    {
        actual.Layer = requested.Layer is null ? null : placement.LayerId;
        actual.Alignment = requested.Alignment is null ? null : ((TextBase)placement).Justification.ToString();
        if (requested.Pen is { } values)
        {
            Pen pen = placement.Pen;
            actual.Pen = values.Keys.ToDictionary(k => k, k => k switch
            {
                "color" => (double)pen.ColorId,
                "width_mm" => pen.Width,
                "style" => (double)pen.StyleId,
                "pattern_length" => pen.PatternLength,
                "line_end" => (double)pen.LineEndType,
                _ => throw new InvalidOperationException(k)
            });
        }
        return actual;
    }
}
