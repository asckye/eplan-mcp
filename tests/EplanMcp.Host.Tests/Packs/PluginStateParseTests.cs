using System.Text;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class PluginStateParseTests
{
    private const string Empty = """{"version":1,"enabled":[],"libraries":[]}""";

    [Fact]
    public void Parse_PreservesOrderedEnabledPluginsAndUncLibraries()
    {
        var expected = new PluginState(1, [new("acme.symbols", "2.0.0"), new("acme.parts", "1.0.0")], [new("shared", @"\\server\library")]);

        PluginState actual = PluginStateStore.Parse(PackJson.Serialize(expected));

        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.Libraries, actual.Libraries);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"enabled\":[]}")]
    [InlineData("{\"version\":1,\"version\":1,\"enabled\":[],\"libraries\":[]}")]
    [InlineData("{\"version\":2,\"enabled\":[],\"libraries\":[]}")]
    [InlineData("{\"version\":1,\"enabled\":null,\"libraries\":[]}")]
    [InlineData("{\"version\":1,\"enabled\":[null],\"libraries\":[]}")]
    [InlineData("{\"version\":1,\"enabled\":[],\"libraries\":[null]}")]
    [InlineData("{\"version\":1,\"enabled\":[],\"libraries\":[],\"extra\":true}")]
    [InlineData("{\"version\":1,\"enabled\":[{\"id\":\"a\",\"version\":\"1.0.0\",\"extra\":true}],\"libraries\":[]}")]
    [InlineData("{\"version\":1,\"enabled\":[{\"id\":null,\"version\":\"1.0.0\"}],\"libraries\":[]}")]
    [InlineData("{\"version\":1,\"enabled\":[],\"libraries\":[{\"name\":\"shared\",\"path\":null}]}")]
    public void Parse_RejectsUnknownDuplicateMissingOrNullMembers(string json) =>
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Parse_RejectsInvalidOrDuplicateIdentities()
    {
        foreach (PluginIdentity[] entries in new PluginIdentity[][]
        {
            [new("ACME", "1.0.0")], [new("acme", "^1.0.0")], [new("acme\n", "1.0.0")], [new("acme", "1.0.0\n")],
            [new("acme", "1.0.0"), new("acme", "2.0.0")],
        })
        {
            Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, entries, []))));
        }
    }

    [Theory]
    [InlineData("relative/library")]
    [InlineData(@"C:relative")]
    [InlineData("C:\\library\nother")]
    [InlineData("")]
    public void Parse_RejectsNonAbsoluteOrControlCharacterPaths(string path) =>
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, [], [new("shared", path)]))));

    [Fact]
    public void Parse_RejectsDuplicateLibraryNamesAndUnknownLibraryMembers()
    {
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, [], [new("shared", @"C:\first"), new("shared", @"C:\second")]))));
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, [], [new("shared\n", @"C:\first")]))));
        string json = Empty.Replace("\"libraries\":[]", "\"libraries\":[{\"name\":\"shared\",\"path\":\"C:\\\\first\",\"extra\":true}]", StringComparison.Ordinal);
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Parse_RejectsOversizedStateAndCollections()
    {
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(new byte[PluginStateStore.MaxFileBytes + 1]));
        PluginIdentity[] enabled = [.. Enumerable.Range(0, 2001).Select(i => new PluginIdentity("p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "1.0.0"))];
        RegisteredPluginLibrary[] libraries = [.. Enumerable.Range(0, 129).Select(i => new RegisteredPluginLibrary("l" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), @"C:\library"))];
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, enabled, []))));
        Assert.Throws<PackFormatException>(() => PluginStateStore.Parse(PackJson.Serialize(new PluginState(1, [], libraries))));
    }
}
