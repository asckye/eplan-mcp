using System.Text;

namespace EplanMcp.Packs.Tests;

[Trait("Size", "Small")]
public sealed class PackDigestTests
{
    [Fact]
    public void Canonical_SortsMembersByUtf16CodeUnits()
    {
        // The example of RFC 8785 section 3.2.3.
        const string json = """
            { "\u20ac": "Euro Sign", "\r": "Carriage Return", "\ufb33": "Hebrew", "1": "One", "\ud83d\ude00": "Emoji", "\u0080": "Control", "\u00f6": "o" }
            """;

        string canonical = PackDigest.Canonical(Encoding.UTF8.GetBytes(json));

        Assert.Equal("{\"\\r\":\"Carriage Return\",\"1\":\"One\",\"\u0080\":\"Control\",\"\u00f6\":\"o\",\"\u20ac\":\"Euro Sign\",\"\ud83d\ude00\":\"Emoji\",\"\ufb33\":\"Hebrew\"}", canonical);
    }

    [Fact]
    public void Canonical_EscapesStringsAsJsonStringify()
    {
        // The string of RFC 8785 section 3.2.2.2's example.
        const string json = """{ "s": "\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"\/", "n": [0, -1, 9007199254740991], "l": [null, true, false] }""";

        Assert.Equal("{\"l\":[null,true,false],\"n\":[0,-1,9007199254740991],\"s\":\"\u20ac$\\u000f\\nA'B\\\"\\\\\\\\\\\"/\"}", PackDigest.Canonical(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Compute_IgnoresLayoutAndMemberOrder()
    {
        byte[] written = PackJson.Serialize(Manifests.Complete());
        string reordered = """{ "name": "x", "schemaVersion": 1 }""";

        Assert.Equal(PackDigest.Compute(written), PackDigest.Compute([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(written).Replace("\n", "\r\n  ", StringComparison.Ordinal))]));
        Assert.Equal(PackDigest.Compute(Encoding.UTF8.GetBytes(reordered)), PackDigest.Compute(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"name\":\"x\"}")));
        Assert.NotEqual(PackDigest.Compute(Encoding.UTF8.GetBytes(reordered)), PackDigest.Compute(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"name\":\"y\"}")));
    }

    [Theory]
    [InlineData("""{ "n": 1.5 }""")]
    [InlineData("""{ "n": 1e3 }""")]
    [InlineData("""{ "n": 9007199254740992 }""")]
    [InlineData("""{ "s": "\ud800" }""")]
    [InlineData("""{ "a": 1, "a": 2 }""")]
    [InlineData("""{ "a": """)]
    public void Canonical_WhatManifestsNeverHold_IsRefused(string json)
    {
        Assert.Throws<PackFormatException>(() => PackDigest.Canonical(Encoding.UTF8.GetBytes(json)));
    }
}
