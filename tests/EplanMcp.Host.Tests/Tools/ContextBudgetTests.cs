using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_context_get budgeting, source de-duplication and null binding metadata without I/O.</summary>
[Trait("Size", "Small")]
public sealed class ContextBudgetTests
{
    [Fact]
    public void NullBinding_DoesNotInventRevisionLockOrGeneration()
    {
        JsonObject reference = ContextBuilder.Reference(null, null);
        Assert.Equal(4, reference.Count);
        Assert.All(reference, p => Assert.Null(p.Value));
    }

    [Fact]
    public void LockDigest_IsSha256OfCanonicalSortedLocks_IndependentOfInputOrder()
    {
        var a = new PackReference { Id = "acme.a", Version = "1.0.0", Digest = new string('a', 64) };
        var b = new PackReference { Id = "acme.b", Version = "2.0.0", Digest = new string('b', 64) };
        var binding = new ProjectBinding { BindingVersion = 1, Project = @"D:\Sample", Revision = 7, Cabinets = [new() { Id = "default" }], Locks = [b, a] };
        string canonical = "[{\"digest\":\"" + a.Digest + "\",\"id\":\"acme.a\",\"version\":\"1.0.0\"},{\"digest\":\"" + b.Digest + "\",\"id\":\"acme.b\",\"version\":\"2.0.0\"}]";
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        JsonObject reference = ContextBuilder.Reference("snapshot", binding);
        Assert.Equal(expected, reference["lockDigest"]!.GetValue<string>());
        Assert.Equal(reference.ToJsonString(), ContextBuilder.Reference("snapshot", binding with
        {
            Locks = [a, b]
        }).ToJsonString());
        Assert.Equal(7, reference["bindingRevision"]!.GetValue<int>());
    }

    [Fact]
    public void FixedIncludeOrder_CleansText_DeduplicatesSources_AndCountsBothCopiesPlusSummary()
    {
        JsonObject result = Fit(20000);
        Assert.Equal(["objects", "facts", "rules"], result.Select(p => p.Key).Where(ContextBuilder.Sections.Contains));
        Assert.Equal(2, result["sources"]!.AsArray().Count);
        Assert.Equal("s-one", result["objects"]![0]!["sources"]![0]!.GetValue<string>());
        Assert.Equal("s-one", result["facts"]![0]!["sources"]![0]!.GetValue<string>());
        Assert.DoesNotContain('\u202e', result.ToJsonString());
        Assert.Equal("motor", result["objects"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(Tokens(result), result["budget"]!["estimatedTokens"]!.GetValue<int>());
        Assert.Equal(0, result["budget"]!["omittedItems"]!.GetValue<int>());
        Assert.False(result["truncated"]!.GetValue<bool>());
        Assert.Null(result["hint"]);
    }

    [Fact]
    public void Budget_StopsBetweenWholeItems_AndRemovesOnlyUnusedSources()
    {
        JsonObject all = Fit(20000);
        int budget = Tokens(all) - 100;
        JsonObject result = Fit(budget);
        Assert.True(result["truncated"]!.GetValue<bool>());
        Assert.InRange(Tokens(result), 1, budget);
        int kept = ContextBuilder.Sections.Where(result.ContainsKey).Sum(s => result[s]!.AsArray().Count);
        Assert.Equal(3 - kept, result["budget"]!["omittedItems"]!.GetValue<int>());
        Assert.Empty(result["rules"]!.AsArray());
        Assert.Single(result["sources"]!.AsArray());
        Assert.NotEmpty(result["hint"]!.GetValue<string>());
        Assert.Equal(Tokens(result), result["budget"]!["estimatedTokens"]!.GetValue<int>());
    }

    [Fact]
    public void EnvelopeBudget_IsMeasured_NoArbitraryMinimum_AndReportsRequiredTokens()
    {
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => Fit(1));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        int minimum = int.Parse(error.Error.Metadata!["minimumEstimatedTokens"], System.Globalization.CultureInfo.InvariantCulture);
        JsonObject envelope = Fit(minimum);
        Assert.InRange(Tokens(envelope), 1, minimum);
        Assert.Equal(3, envelope["budget"]!["omittedItems"]!.GetValue<int>());
        Assert.Empty(envelope["sources"]!.AsArray());
    }

    private static int Tokens(JsonObject result) => ResultShaper.ResultTokens(result.ToJsonString(HostJson.Options)) + ResultShaper.EstimateTokens(ContextBuilder.Summary(result));

    private static JsonObject Fit(int budget)
    {
        var result = new JsonObject { ["coverage"] = new JsonObject { ["pages"] = new JsonArray() }, ["sources"] = new JsonArray() };
        var source = new JsonObject { ["id"] = "s-one", ["hash"] = new string('a', 64) };
        var other = new JsonObject { ["id"] = "s-two", ["hash"] = new string('b', 64) };
        var items = ContextBuilder.Sections.ToDictionary(s => s, _ => new List<(JsonObject Item, JsonObject Source)>(), StringComparer.Ordinal);
        items["objects"].Add((new JsonObject { ["name"] = "motor\u202e" }, source));
        items["facts"].Add((new JsonObject { ["value"] = "24 V" }, source));
        items["rules"].Add((new JsonObject { ["title"] = new string('x', 500) }, other));
        return ContextBuilder.Fit(result, items, ["rules", "facts", "objects"], budget);
    }
}
