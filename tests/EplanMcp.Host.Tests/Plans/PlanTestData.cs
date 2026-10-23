using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

internal static class PlanTestData
{
    public static readonly DateTimeOffset Created = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    public static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    public static PlanDefinition Definition()
    {
        var scope = new PlanScope(["+A/1"], ["+A"], [new("PROJECT", "page", 1, 42, "+A/1", "+A/1", null, null)]);
        JsonElement args = Json("""{"page":"+A/1","description":"Example","language":"en_US"}""");
        return new(
            "0123456789abcdef0123456789abcdef", new("Demo", "PROJECT", null), "default", new("host", "session"),
            Created, Created.AddHours(24), 1, "1", 1, "2026", "Pro Panel", ["page.update"],
            new([new("page.update", args)]), null, null, null,
            [new("example", "1.0.0", "macros/example.ema", "manifest", "file")],
            [new("s1", "PROJECT", "page", 1, 42, "object", null, null, null, null, null)],
            new("pages", "objects", "names", "changes"),
            [new(0, "page.update", args, ToolTier.Write, scope, scope,
                [new PlanObjectHash(scope.Locators[0], "object")],
                [Json("""{"description":"Example","source":"s1"}""")],
                [new("update", "page", "+A/1", "description", null, "Example")], [], null, true)]);
    }
}
