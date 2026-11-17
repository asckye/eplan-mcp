using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
namespace EplanMcp.Host.Tools;

internal static class ApiTrySettingsResult
{
    internal static string? Text(string group, JsonElement parameters, string? value)
    {
        if (value is null || !ApiTryCatalog.Settings(group) || !group.Contains(".GetListOf", StringComparison.Ordinal))
        { return ResultShaper.CleanText(value); }
        JsonObject page = JsonNode.Parse(value)!.AsObject();
        JsonArray items = page["items"]!.AsArray();
        foreach (int i in Enumerable.Range(0, items.Count))
        { items[i] = ResultShaper.CleanText(items[i]!.GetValue<string>()); }
        int offset = parameters.GetProperty("offset").GetInt32(), total = page["total"]!.GetValue<int>();
        while (items.Count > 1 && ResultShaper.EstimateTokens(page.ToJsonString()) > 2000)
        { items.RemoveAt(items.Count - 1); }
        page["nextOffset"] = offset + items.Count < total ? JsonValue.Create(offset + items.Count) : null;
        return page.ToJsonString();
    }
}
