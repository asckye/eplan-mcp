using System.Text.Json;
using System.Xml.Linq;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ActionRegistryParsingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Actions", "action", "C-candidate")]
    [InlineData("Interactions", "interaction", "excluded")]
    [InlineData("AsmActions", "asm_action", "C-candidate")]
    public void RegistryKinds_ReadExactServicePathAndFirstNonemptyModule(string category, string kind, string tier)
    {
        var document = XDocument.Parse($"""
            <Settings><CAT><MOD name="FixtureModule"><LEV1 name="Services"><LEV2 name="{category}">
              <LEV3 name="FixtureRegistered"><LEV4><Setting name="ModuleName"><Val> </Val></Setting></LEV4>
                <LEV4><Setting name="ModuleName"><Val>FixtureAssembly</Val></Setting></LEV4>
                <LEV4><Setting name="ModuleName"><Val>IgnoredAssembly</Val></Setting></LEV4></LEV3>
            </LEV2></LEV1></MOD></CAT></Settings>
            """);
        ActionCatalogItem row = Assert.Single(ActionCatalogReader.ParseRegistry(document, "regPlatform.xml", Token));
        Assert.Equal("FixtureRegistered", row.Name);
        Assert.Equal("registry", row.Source);
        Assert.Equal(kind, row.RegistryKind);
        Assert.Equal("FixtureAssembly", row.Module);
        Assert.Equal(tier, row.Tier);
        Assert.Equal(kind == "interaction" ? "interaction: not callable by name; use a drawing tool" : null, row.ExcludedReason);
    }

    [Theory]
    [InlineData("Classes")]
    [InlineData("Dialogs")]
    [InlineData("Interfaces")]
    [InlineData("Messages")]
    [InlineData("DialogBarFactories")]
    [InlineData("DialogFactories")]
    [InlineData("EplanInfoBarFactory")]
    [InlineData("FutureCategory")]
    public void NonActionRegistryCategories_AreIgnored(string category) =>
        Assert.Empty(ActionCatalogReader.ParseRegistry(XDocument.Parse(ActionRegistryFixture.Xml(category, "FixtureIgnored")), "regPlatform.xml", Token));

    [Fact]
    public void WrongServicePaths_AreIgnored()
    {
        var document = XDocument.Parse("""
            <Settings><CAT><MOD name="FixtureModule"><LEV1 name="Other"><LEV2 name="Actions"><LEV3 name="FixtureOther"/></LEV2></LEV1></MOD></CAT>
              <Wrapper><CAT><MOD name="FixtureModule"><LEV1 name="Services"><LEV2 name="Actions"><LEV3 name="FixtureNested"/></LEV2></LEV1></MOD></CAT></Wrapper>
            </Settings>
            """);
        Assert.Empty(ActionCatalogReader.ParseRegistry(document, "regPlatform.xml", Token));
    }

    [Fact]
    public void MissingModule_IsNullAndMftoolsDialogsAndShortcutsAreIgnored()
    {
        var registry = XDocument.Parse("""
            <Settings><CAT><MOD name="FixtureModule"><LEV1 name="Services"><LEV2 name="Actions"><LEV3 name="FixtureNoModule"/></LEV2></LEV1></MOD></CAT></Settings>
            """);
        Assert.Null(Assert.Single(ActionCatalogReader.ParseRegistry(registry, "regMinimal.xml", Token)).Module);
        var ui = XDocument.Parse("""
            <Settings><LEV1 name="Dialogs"><Setting name="1"><Val>FixtureDialog</Val></Setting></LEV1>
              <LEV1 name="Shortcuts"><Setting name="2"><Val>FixtureShortcut</Val></Setting></LEV1></Settings>
            """);
        Assert.Empty(ActionCatalogReader.Parse(ui, "MFTools.xml", Token));
    }
}

[Trait("Size", "Medium")]
public sealed class ActionRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eplan-action-registry-" + Guid.NewGuid().ToString("N"));
    private readonly ActionCatalogReader _reader;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ActionRegistryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Bin"));
        Directory.CreateDirectory(Path.Combine(_root, "Cfg"));
        _reader = new(() => (Path.Combine(_root, "Bin"), "2026.0.3.25421"));
    }

    [Fact]
    public async Task RegistryOnlyAction_IsDiscoveredOutsideUiDefinitionEnumeration()
    {
        await WriteAsync("regPlatform.xml", ActionRegistryFixture.Xml("Actions", "FixtureRegistryAction"));
        ActionCatalogItem row = Assert.Single((await _reader.ReadAsync(Token)).Items, i => i.Name == "FixtureRegistryAction");
        Assert.Equal("registry", row.Source);
        Assert.Equal("action", row.RegistryKind);
        Assert.Equal("FixtureAssembly", row.Module);
    }

    [Fact]
    public async Task BothRegistryFilesAndMultipleModules_AreReadAndDuplicateNamesAreMerged()
    {
        var platform = XDocument.Parse(ActionRegistryFixture.Xml("Actions", "FixtureShared"));
        platform.Root!.Element("CAT")!.Add(XDocument.Parse(ActionRegistryFixture.Xml("AsmActions", "FixtureAssemblyAction")).Root!.Element("CAT")!.Element("MOD"));
        await WriteAsync("regPlatform.xml", platform.ToString());
        await WriteAsync("regMinimal.xml", ActionRegistryFixture.Xml("Actions", "FixtureShared"));
        ActionCatalogItem[] rows = [.. (await _reader.ReadAsync(Token)).Items.Where(i => i.Source == "registry")];
        Assert.Equal(2, rows.Length);
        Assert.Single(rows, i => i.Name == "FixtureShared");
        Assert.Equal("asm_action", Assert.Single(rows, i => i.Name == "FixtureAssemblyAction").RegistryKind);
    }

    [Fact]
    public async Task RegistryMetadata_EnrichesAllOfficialAndUiRowsWithoutMutatingOfficialCache()
    {
        ActionCatalogItem known = ActionCatalogReader.Official().First(i => i.Tier == "A");
        await WriteAsync("regPlatform.xml", ActionRegistryFixture.Xml("Actions", known.Name));
        await WriteAsync("MFTools.xml", new XElement("Settings", new XElement("Button", new XAttribute("action", Command(known)))).ToString());
        ActionCatalogItem[] rows = [.. (await _reader.ReadAsync(Token)).Items.Where(i => i.Name == known.Name)];
        Assert.Equal(ActionCatalogReader.Official().Count(i => i.Name == known.Name) + 1, rows.Length);
        Assert.DoesNotContain(rows, i => i.Source == "registry");
        Assert.All(rows, i =>
        {
            Assert.Equal("action", i.RegistryKind);
            Assert.Equal("FixtureAssembly", i.Module);
        });
        Assert.Equal(known.Tier, Assert.Single(rows, i => i.Source == "official" && i.Type == known.Type).Tier);
        Assert.Equal(known.Tier, Assert.Single(rows, i => i.Source == "ui").Tier);
        JsonElement official = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "official", registry_kind: "action", cancellationToken: Token));
        Assert.Equal(rows.Count(i => i.Source == "official"), official.GetProperty("total").GetInt32());
        JsonElement ui = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "ui", registry_kind: "action", cancellationToken: Token));
        Assert.Equal(known.Name, Assert.Single(ui.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.All(ActionCatalogReader.Official(), i => Assert.Null(i.RegistryKind));
    }

    [Fact]
    public async Task NameInActionsAndInteractions_StaysCallable_InteractionOnlyIsExcluded()
    {
        ActionCatalogItem known = ActionCatalogReader.Official().First(i => i.Tier == "A");
        await WriteAsync("regPlatform.xml", ActionRegistryFixture.Xml("Actions", known.Name));
        await WriteAsync("regMinimal.xml", ActionRegistryFixture.Xml("Interactions", known.Name));
        await WriteAsync("MFTools.xml", new XElement("Settings", new XElement("Button", new XAttribute("action", Command(known)))).ToString());
        ActionCatalogItem[] both = [.. (await _reader.ReadAsync(Token)).Items.Where(i => i.Name == known.Name)];
        Assert.NotEmpty(both);
        Assert.DoesNotContain(both, i => i.Source == "registry");
        Assert.All(both, i =>
        {
            Assert.Equal("action", i.RegistryKind);
            Assert.Null(i.ExcludedReason);
        });

        await WriteAsync("regPlatform.xml", ActionRegistryFixture.Xml("Interactions", known.Name));
        ActionCatalogItem[] interaction = [.. (await _reader.ReadAsync(Token)).Items.Where(i => i.Name == known.Name)];
        Assert.All(interaction, i =>
        {
            Assert.Equal("interaction", i.RegistryKind);
            Assert.Equal("excluded", i.Tier);
            Assert.Equal("interaction: not callable by name; use a drawing tool", i.ExcludedReason);
        });
    }

    [Fact]
    public async Task MissingRegistryFiles_ReportEachGapAndKeepOfficialRows()
    {
        ActionCatalogData data = await _reader.ReadAsync(Token);
        Assert.Equal(ActionCatalogReader.Official().Length, data.Items.Length);
        Assert.Contains(data.Gaps, g => g.Contains("regPlatform.xml", StringComparison.Ordinal));
        Assert.Contains(data.Gaps, g => g.Contains("regMinimal.xml", StringComparison.Ordinal));
        Assert.DoesNotContain(data.Gaps, g => g.Contains(_root, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<broken>")]
    [InlineData("<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///must-not-be-read'>]><Settings>&leak;</Settings>")]
    public async Task InvalidRegistryXml_ReportsGapAndContinuesReadingOtherFile(string xml)
    {
        await WriteAsync("regPlatform.xml", xml);
        await WriteAsync("regMinimal.xml", ActionRegistryFixture.Xml("Actions", "FixtureSurvivor"));
        ActionCatalogData data = await _reader.ReadAsync(Token);
        Assert.Contains(data.Gaps, g => g.Contains("regPlatform.xml", StringComparison.Ordinal));
        Assert.Contains(data.Items, i => i.Name == "FixtureSurvivor");
        Assert.Equal(ActionCatalogReader.Official().Length, data.Items.Count(i => i.Source == "official"));
    }

    [Fact]
    public async Task UnreadableRegistry_ReportsGapAndContinuesReadingOtherFile()
    {
        await WriteAsync("regPlatform.xml", ActionRegistryFixture.Xml("Actions", "FixtureLocked"));
        await WriteAsync("regMinimal.xml", ActionRegistryFixture.Xml("Actions", "FixtureSurvivor"));
        using var locked = new FileStream(Path.Combine(_root, "Cfg", "regPlatform.xml"), FileMode.Open, FileAccess.Read, FileShare.None);
        ActionCatalogData data = await _reader.ReadAsync(Token);
        Assert.Contains(data.Gaps, g => g.Contains("regPlatform.xml", StringComparison.Ordinal));
        Assert.Contains(data.Items, i => i.Name == "FixtureSurvivor");
        Assert.DoesNotContain(data.Items, i => i.Name == "FixtureLocked");
    }

    [Fact]
    public async Task SourceAndKindFilters_IntersectAndBindPaginationCursor()
    {
        var platform = XDocument.Parse(ActionRegistryFixture.Xml("Actions", "FixtureActionOne"));
        XElement module = platform.Root!.Element("CAT")!.Element("MOD")!;
        module.Add(XDocument.Parse(ActionRegistryFixture.Xml("Actions", "FixtureActionTwo")).Descendants("LEV1").Single());
        module.Add(XDocument.Parse(ActionRegistryFixture.Xml("AsmActions", "FixtureAssemblyAction")).Descendants("LEV1").Single());
        await WriteAsync("regPlatform.xml", platform.ToString());
        await WriteAsync("regMinimal.xml", ActionRegistryFixture.Xml("Interactions", "FixtureInteraction"));
        JsonElement first = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "registry", registry_kind: "action", limit: 1, cancellationToken: Token));
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        string cursor = first.GetProperty("nextCursor").GetString()!;
        JsonElement second = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "registry", registry_kind: "action", limit: 1, cursor: cursor, cancellationToken: Token));
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("name").GetString(), second.GetProperty("items")[0].GetProperty("name").GetString());
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(_reader, source: "registry", registry_kind: "asm_action", limit: 1, cursor: cursor, cancellationToken: Token));
        JsonElement excluded = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "registry", registry_kind: "interaction", tier: "excluded", text: "FIXTURE", cancellationToken: Token));
        Assert.Equal("FixtureInteraction", Assert.Single(excluded.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        JsonElement get = ApiToolTests.Data(await ActionCatalogTools.GetAsync(_reader, "FixtureInteraction", cancellationToken: Token));
        JsonElement template = Assert.Single(get.GetProperty("items").EnumerateArray()).GetProperty("try_template");
        Assert.True(template.GetProperty("denied").GetBoolean());
        Assert.Equal("interaction: not callable by name; use a drawing tool", template.GetProperty("exclusion_reason").GetString());
        JsonElement noMatch = ApiToolTests.Data(await ActionCatalogTools.SearchAsync(_reader, source: "ui", registry_kind: "action", cancellationToken: Token));
        Assert.Equal(0, noMatch.GetProperty("total").GetInt32());
        var unread = new ActionCatalogReader(() => throw new InvalidOperationException("Must validate first"));
        ApiToolTests.AssertError(await ActionCatalogTools.SearchAsync(unread, registry_kind: "Actions", cancellationToken: Token));
    }

    private static string Command(ActionCatalogItem item) => item.Name + (item.Type is null ? string.Empty : " /TYPE:" + item.Type);
    private Task WriteAsync(string file, string xml) => File.WriteAllTextAsync(Path.Combine(_root, "Cfg", file), xml, Token);
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal static class ActionRegistryFixture
{
    internal static string Xml(string kind, string name) => $"""
        <Settings><CAT><MOD name="FixtureModule"><LEV1 name="Services"><LEV2 name="{kind}">
          <LEV3 name="{name}"><LEV4><Setting name="ModuleName"><Val>FixtureAssembly</Val></Setting></LEV4></LEV3>
        </LEV2></LEV1></MOD></CAT></Settings>
        """;
}
