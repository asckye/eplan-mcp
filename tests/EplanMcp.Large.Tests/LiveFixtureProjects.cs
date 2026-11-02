using System.Text.Json;

namespace EplanMcp.Large.Tests;

internal static class LiveFixtureProjects
{
    internal static string Resolve(JsonElement result, string expected)
    {
        string[] paths = [.. result.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("path").GetString()!)];
        return paths.SingleOrDefault(p => string.Equals(p, expected, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The EPLAN fixture {expected} is not open: open it with eng/scripts/eval-fixture.ps1 -Open.");
    }
}
