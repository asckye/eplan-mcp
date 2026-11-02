using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class AcceptanceReviewTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("objects")]
    [InlineData("position")]
    public void WarningDeclarations_CannotAllowErrorsOrOmitEvidence(string change)
    {
        JsonNode list = JsonNode.Parse(AcceptanceDrawingListTests.Fixture())!;
        JsonNode warning = list["expected_review_warnings"]![0]!;
        warning[change] = change switch { "code" => JsonValue.Create("dangling_end"), "objects" => new JsonArray(), _ => new JsonArray(0) };
        Assert.Contains("/expected_review_warnings/0", Assert.Throws<JsonException>(() => AcceptanceDrawingList.Read(list.ToJsonString())).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("error")]
    [InlineData("position")]
    [InlineData("object")]
    public void FictionalDrawing_ExactlyFiveSparePinsAndTwentyEnvelopeOverlaps_NoOtherProblems(string? change)
    {
        JsonElement list = AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture());
        const string Page = "=GAA+A1&EFS1/3";
        JsonElement[] expected = list.GetProperty("expected_review_warnings").EnumerateArray().ToArray();
        Assert.Equal(5, expected.Count(w => w.GetProperty("code").GetString() == "unconnected_pin"));
        Assert.Equal(20, expected.Count(w => w.GetProperty("code").GetString() == "overlap"));
        var drawings = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var issues = new List<JsonObject>();
        foreach (JsonElement warning in expected)
        {
            var locators = new JsonArray();
            foreach (JsonElement obj in warning.GetProperty("objects").EnumerateArray())
            {
                string key = obj.GetRawText();
                if (!drawings.TryGetValue(key, out JsonObject? drawing))
                {
                    drawing = new()
                    {
                        ["kind"] = obj.GetProperty("kind").GetString(),
                        ["location"] = JsonNode.Parse(obj.GetProperty("position").GetRawText()),
                        ["properties"] = new JsonObject { ["name"] = obj.GetProperty("name").GetString()!.Replace("{run}", "3", StringComparison.Ordinal) },
                        ["locator"] = new JsonObject { ["project"] = "LargeWrite", ["page"] = Page, ["type"] = 17, ["oid"] = drawings.Count + 1, ["digest"] = new string('a', 64) },
                    };
                    drawings.Add(key, drawing);
                }
                locators.Add(drawing["locator"]!.DeepClone());
            }
            issues.Add(new() { ["page"] = Page, ["severity"] = "warning", ["code"] = warning.GetProperty("code").GetString(), ["position"] = JsonNode.Parse(warning.GetProperty("position").GetRawText()), ["objects"] = locators });
        }
        switch (change)
        {
            case "missing":
                issues.RemoveAt(0);
                break;
            case "extra":
                issues.Add(issues[0].DeepClone().AsObject());
                break;
            case "error":
                issues[0]["severity"] = "error";
                break;
            case "position":
                issues[0]["position"] = new JsonArray(0, 0);
                break;
            case "object":
                drawings.Values.First()["properties"]!["name"] = "-Unexpected";
                break;
        }
        void Verify() => AcceptanceReview.Verify(list, "3", Page, issues.Select(i => JsonSerializer.SerializeToElement(i)).Reverse().ToArray(), drawings.Values.Select(d => JsonSerializer.SerializeToElement(d)).ToArray());
        if (change is null)
        { Verify(); }
        else
        { Assert.ThrowsAny<Xunit.Sdk.XunitException>(Verify); }
    }
}
