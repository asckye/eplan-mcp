using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
namespace EplanMcp.Host.Tools;

/// <summary>Per-Host detached observations, bounded to 16 results for ten minutes; paging never repeats an API call.</summary>
internal sealed class ApiTryPages(TimeProvider clock)
{
    private sealed record Entry(string Query, DateTimeOffset Expiry, JsonElement[] Items, int Budget);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    internal object? Shape(string? json, string query)
    {
        if (json is null)
        { return null; }
        JsonElement value = JsonSerializer.Deserialize<JsonElement>(json);
        return Shape(value, query, (ResultShaper.DefaultTokenBudget - 1500) / Math.Max(1, Arrays(value)));
    }
    private static int Arrays(JsonElement value) => value.ValueKind == JsonValueKind.Array ? 1 : value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Sum(p => Arrays(p.Value)) : 0;
    private object? Shape(JsonElement value, string query, int budget) => value.ValueKind switch
    {
        JsonValueKind.Array => Store(value.EnumerateArray().Select(Clean).ToArray(), query, budget),
        JsonValueKind.Object when value.TryGetProperty("digest", out _) => value.Clone(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Shape(p.Value, query, budget), StringComparer.Ordinal),
        JsonValueKind.String => ResultShaper.CleanText(value.GetString()),
        _ => value.Clone(),
    };
    private static JsonElement Clean(JsonElement value) => value.ValueKind == JsonValueKind.String ? JsonSerializer.SerializeToElement(ResultShaper.CleanText(value.GetString())) : value.Clone();
    private ApiTryResultPage Store(JsonElement[] items, string query, int budget)
    {
        lock (_entries)
        {
            foreach (string key in _entries.Where(p => p.Value.Expiry <= clock.GetUtcNow()).Select(p => p.Key).ToArray())
            { _entries.Remove(key); }
            if (_entries.Count >= 16)
            { _entries.Remove(_entries.MinBy(p => p.Value.Expiry).Key); }
            string id = Guid.NewGuid().ToString("N");
            _entries[id] = new(query, clock.GetUtcNow().AddMinutes(10), items, budget);
            return Page(id, _entries[id], 0);
        }
    }
    internal ApiTryResultPage Read(string cursor, string query)
    {
        lock (_entries)
        {
            string[] parts = (cursor.Length <= 1024 ? cursor : string.Empty).Split('.');
            if (parts.Length == 2 && _entries.TryGetValue(parts[0], out Entry? entry) && entry.Query == query && entry.Expiry > clock.GetUtcNow()
                && PageCursor.Decode(parts[1], parts[0]) is int offset && offset > 0 && offset < entry.Items.Length)
            { return Page(parts[0], entry, offset); }
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.InvalidArgument, Execution = ExecutionState.NotStarted, Message = "result_cursor is expired or belongs to another call/Host; do not repeat a write to retrieve its result." });
        }
    }
    private static ApiTryResultPage Page(string id, Entry entry, int offset)
    {
        var page = new ApiTryResultPage { Items = entry.Items.Skip(offset).ToList(), Total = entry.Items.Length };
        ResultShaper.FitPage(page, offset, id, entry.Budget);
        if (page.NextCursor is not null)
        { page.NextCursor = id + "." + page.NextCursor; page.Hint = "Pass nextCursor as result_cursor with the same group/project/parameters; this serves cached observations without invoking EPLAN."; }
        return page;
    }
}
internal sealed class ApiTryResultPage : IPagedResult<JsonElement>
{
    public List<JsonElement> Items { get; init; } = [];
    public int Total { get; init; }
    public string? NextCursor { get; set; }
    public bool Truncated { get; set; }
    public string? Hint { get; set; }
}
