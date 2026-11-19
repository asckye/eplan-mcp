using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Packs;

namespace EplanMcp.Host.Tools;

internal sealed class PageReviewLayout
{
    private readonly Dictionary<string, string> _plugins = new(StringComparer.Ordinal);
    internal JsonObject Values { get; } = new();
    internal List<string> Warnings { get; } = [];

    internal static PageReviewLayout Read(PluginResolution resolution, bool scratch)
    {
        var layout = new PageReviewLayout();
        Add("review.layout.parallel_spacing_mm", "parallelSpacingMm");
        Add("review.layout.minimum_text_height_mm", "minimumTextHeightMm", positive: true);
        Add("review.layout.margin_mm", "marginMm");
        return layout;

        void Add(string key, string member, bool positive = false)
        {
            JsonElement? value = PluginConsumerDefaults.Value(resolution, key);
            string? identity = PluginConsumerDefaults.Plugins(resolution, key).FirstOrDefault();
            if (value is null && identity is null)
            { return; }
            if (identity is null)
            {
                layout.Warnings.Add($"{key} has no supplying plugin ID. Using the page configuration or default.");
                return;
            }
            if (value is not { ValueKind: JsonValueKind.Number } number || !number.TryGetDouble(out double mm)
                || mm is not (>= 0 and <= 100) || positive && mm == 0)
            {
                string range = positive ? "(0, 100]" : "[0, 100]";
                layout.Warnings.Add($"{identity}: {key} requires a number in {range} mm. Using the page configuration or default.");
                return;
            }
            string id = identity.Split('@')[0];
            if (!scratch && !resolution.Enabled.Any(p => $"{p.Id}@{p.Version}" == identity && p.Manifest?.Signoff?.Status == "signed"))
            {
                layout.Warnings.Add($"{identity}: {key} requires a signed plugin outside scratch projects. Using the page configuration or default.");
                return;
            }
            layout.Values[member] = mm;
            layout._plugins.Add(member, id);
        }
    }

    internal object Request(string project, string page, string[]? checks, JsonElement? expected, double tolerance_mm) =>
        Values.Count == 0 ? new { project, page, checks, expected, tolerance_mm }
            : new { project, page, checks, expected, tolerance_mm, layout = Values };

    internal void ApplySources(JsonObject data)
    {
        foreach ((string member, string id) in _plugins)
        {
            if (data["layout"]?["sources"]?[member]?.GetValue<string>() == "request")
            { data["layout"]!["sources"]![member] = "plugin:" + id; }
            if (data["layoutWarnings"]?[member] is JsonNode warning)
            { Warnings.Add($"{id}: {warning.GetValue<string>()} Using the page configuration or default."); }
        }
        data.Remove("layoutWarnings");
    }
}
