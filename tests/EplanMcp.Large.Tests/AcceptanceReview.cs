using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>The drawing specifies spare pins and overlapping envelopes. Assert the complete warning multiset, never an error allowance.</summary>
internal static class AcceptanceReview
{
    internal static void Verify(JsonElement list, string run, string page, JsonElement[] issues, JsonElement[] drawings)
    {
        JsonElement[] expected = list.TryGetProperty("expected_review_warnings", out JsonElement warnings) ? warnings.EnumerateArray().ToArray() : [];
        var remaining = issues.Where(i => i.GetProperty("severity").GetString() != "info").ToList();
        Assert.Equal(expected.Length, remaining.Count);
        foreach (JsonElement want in expected)
        {
            string Expand(string value) => value.Replace("{run}", run, StringComparison.Ordinal);
            static bool Position(JsonElement a, JsonElement b) => a.EnumerateArray().Select(n => n.GetDouble()).SequenceEqual(b.EnumerateArray().Select(n => n.GetDouble()));
            bool Objects(JsonElement issue)
            {
                var observed = issue.GetProperty("objects").EnumerateArray().Select(locator => Assert.Single(drawings,
                    d => JsonElement.DeepEquals(d.GetProperty("locator"), locator))).ToList();
                foreach (JsonElement item in want.GetProperty("objects").EnumerateArray())
                {
                    JsonElement[] matching = observed.Where(d => d.GetProperty("kind").GetString() == item.GetProperty("kind").GetString()
                        && Position(item.GetProperty("position"), d.GetProperty("location"))
                        && DrawingNames.SameName(Expand(item.GetProperty("name").GetString()!), d.GetProperty("properties").GetProperty("name").GetString()!, page)).ToArray();
                    if (matching.Length != 1)
                    { return false; }
                    observed.Remove(matching[0]);
                }
                return observed.Count == 0;
            }
            JsonElement match = Assert.Single(remaining, i => i.GetProperty("severity").GetString() == "warning"
                && i.GetProperty("page").GetString() == page && i.GetProperty("code").GetString() == want.GetProperty("code").GetString()
                && Position(want.GetProperty("position"), i.GetProperty("position")) && Objects(i));
            remaining.Remove(match);
        }
        Assert.Empty(remaining);
    }
}
