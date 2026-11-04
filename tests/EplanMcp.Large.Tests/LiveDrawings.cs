using System.Text.Json;

namespace EplanMcp.Large.Tests;

internal static class LiveDrawings
{
    /// <summary>Every drawing row of a page, as one array: a single eplan_page_get response is cut to its size budget.</summary>
    internal static async Task<JsonElement> ReadAllAsync(Func<string, Dictionary<string, object?>, Task<JsonElement>> call, string project, string page)
    {
        var rows = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var args = new Dictionary<string, object?> { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200 };
            if (cursor is not null)
            { args["drawing_cursor"] = cursor; }
            JsonElement drawings = (await call("eplan_page_get", args)).GetProperty("drawings");
            rows.AddRange(drawings.GetProperty("items").EnumerateArray());
            cursor = drawings.TryGetProperty("nextCursor", out JsonElement next) ? next.GetString() : null;
        } while (cursor is not null);
        return JsonSerializer.SerializeToElement(rows);
    }
}
