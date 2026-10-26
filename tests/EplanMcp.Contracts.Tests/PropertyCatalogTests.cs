using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

[Trait("Size", "Small")]
public sealed class PropertyCatalogTests
{
    [Fact]
    public void EmbeddedCatalog_IncludesUnknownIdsWithoutInventingConstants()
    {
        Assert.Equal("2026.0.3", PropertyCatalog.CatalogVersion);
        Assert.Equal(9521, PropertyCatalog.Entries.Count);
        Assert.Equal(45, PropertyCatalog.Entries.Count(e => e.PropertyId is null));
        PropertyDictionaryEntry entry = Assert.IsType<PropertyDictionaryEntry>(
            PropertyCatalog.Find("MDFunctionTemplatePosition", "FUNC_TECHNICAL_CHARACTERISTIC"));
        Assert.Null(entry.PropertyId);
        Assert.Equal("no_public_id", entry.ReadRefusal);
        Assert.Contains(PropertyCatalog.Entries, e => e.Obsolete);
        Assert.All(PropertyCatalog.Entries.Where(e => e.Obsolete), e => Assert.NotNull(e.ReadRefusal));
    }

    [Fact]
    public void Lookup_ExactTypeAndFormalName_ResolvesWithoutGrantingWrites()
    {
        PropertyDictionaryEntry entry = Assert.IsType<PropertyDictionaryEntry>(PropertyCatalog.Find("Function", "FUNC_TEXT"));
        Assert.Equal(20011, entry.PropertyId);
        Assert.Same(entry, PropertyCatalog.Find("Function", 20011));
        Assert.Null(entry.ReadRefusal);
        Assert.Null(PropertyCatalog.Find("Page", 20011));
        Assert.Null(PropertyCatalog.Find("function", "FUNC_TEXT"));
        Assert.Equal([(2026, "Function", 20011, ""), (2026, "Function", 20027, "")], PropertyWriteWhitelist.Entries.Keys.OrderBy(k => k.PropertyId));
    }

    [Theory]
    [InlineData("Function text")]
    [InlineData("功能文本")]
    [InlineData("func_text")]
    [InlineData("FUNC_TEX")]
    [InlineData("FUNC_TEXT*")]
    [InlineData("Custom.Property")]
    public void Lookup_LocalizedFuzzyOrCustomName_IsNotResolved(string name) =>
        Assert.Null(PropertyCatalog.Find("Function", name));

    [Theory]
    [InlineData("Page", 3010)]
    [InlineData("Project", 3013)]
    [InlineData("Symbol", 3011)]
    [InlineData("MDSymbolLibrary", 3012)]
    [InlineData("StorableObject", 2002)]
    [InlineData("MacroBox", 2001)]
    public void ReadClassification_UserMetadata_IsExcludedOnItsExactObjectType(string objectType, int id)
    {
        Assert.Equal("user_metadata", PropertyCatalog.Find(objectType, id)!.ReadRefusal);
        Assert.Equal("user_metadata", PropertyCatalog.ReadRefusal(objectType, id));
    }

    [Fact]
    public void ReadClassification_AllPropUserRows_AreExcludedBeforeValueAccess()
    {
        PropertyDictionaryEntry[] userRows = [.. PropertyCatalog.Entries.Where(e => e.PropertyName.StartsWith("PROPUSER_", StringComparison.Ordinal))];
        Assert.True(userRows.Select(e => e.ObjectType).Distinct(StringComparer.Ordinal).Count() >= 10);
        Assert.All(userRows, e =>
        {
            Assert.NotNull(e.ReadRefusal);
            Assert.NotNull(PropertyCatalog.ReadRefusal(e.ObjectType, e.PropertyId!.Value));
        });
    }

    [Theory]
    [InlineData("StorableObject")]
    [InlineData("MacroBox")]
    [InlineData("Placement3D")]
    [InlineData("Function")]
    [InlineData("NewObjectType")]
    public void ReadClassification_RuntimeId2000_IsAlwaysRefused(string objectType) =>
        Assert.Equal("runtime_identifier", PropertyCatalog.ReadRefusal(objectType, 2000));

    [Theory]
    [InlineData("Project", "PROJ_FULL_PROJECTPATH", "path")]
    [InlineData("Project", "PROJ_PROJECTPATH", "path")]
    [InlineData("HyperLink", "FILE", "path")]
    [InlineData("Page", "PAGE_FORMPLOT", "path")]
    [InlineData("Page", "PAGE_CREATOR", "personal")]
    [InlineData("Project", "PROJ_CREATOREMAIL", "personal")]
    [InlineData("Project", "PROJ_RESPONSIBLEFORPROJECT", "personal")]
    [InlineData("MDPartsDatabaseItem", "PART_ADDRESS_EMAIL", "personal")]
    public void ReadClassification_PathsAndContacts_AreShippedExclusions(string objectType, string name, string classification)
    {
        PropertyDictionaryEntry entry = Assert.IsType<PropertyDictionaryEntry>(PropertyCatalog.Find(objectType, name));
        Assert.Equal(classification, entry.ReadRefusal);
        Assert.Equal(classification, PropertyCatalog.ReadRefusal(objectType, entry.PropertyId!.Value));
    }

    [Fact]
    public void ReadClassification_CatalogRowsReadableUnlessExcluded_UnknownStayUnreviewed()
    {
        // Rows outside this reviewed catalog version, such as a later version's additions, are refused.
        Assert.Equal("unreviewed", PropertyCatalog.ReadRefusal("NewObjectType", 20011));
        Assert.Equal("unreviewed", PropertyCatalog.ReadRefusal("Function", int.MaxValue));
        Assert.Null(PropertyCatalog.ReadRefusal("Function", 20011));
        Assert.Null(PropertyCatalog.Find("Function", "FUNC_PLCADDRESS")!.ReadRefusal);
        Assert.Equal("user_metadata", PropertyCatalog.ReadRefusal("Page", 3013));
        Assert.All(PropertyCatalog.Entries.Where(e => e.ReadRefusal is null), e =>
            Assert.True(!e.Obsolete && e.PropertyId is not null && !e.PropertyName.StartsWith("PROPUSER_", StringComparison.Ordinal)));
    }

    [Fact]
    public void CatalogRows_CannotBeReplacedThroughThePublicCollection()
    {
        var mutable = Assert.IsAssignableFrom<IList<PropertyDictionaryEntry>>(PropertyCatalog.Entries);
        Assert.Throws<NotSupportedException>(() => mutable.RemoveAt(0));
    }

    [Fact]
    public void Violation_BothSerializers_RoundTripJsonPointerAndReason()
    {
        var original = new PropertyViolation
        {
            Path = "/queries/0/properties/1/property_name",
            Reason = ErrorReason.PolicyDenied,
            Message = "This property is excluded by the shipped read classification.",
        };
        var host = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var bridge = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        string hostJson = System.Text.Json.JsonSerializer.Serialize(original, host);
        string bridgeJson = JsonConvert.SerializeObject(original, bridge);
        Assert.Equal(hostJson, bridgeJson);
        PropertyViolation fromHost = JsonConvert.DeserializeObject<PropertyViolation>(hostJson, bridge)!;
        PropertyViolation fromBridge = System.Text.Json.JsonSerializer.Deserialize<PropertyViolation>(bridgeJson, host)!;
        Assert.Equal(original.Path, fromHost.Path);
        Assert.Equal(original.Reason, fromHost.Reason);
        Assert.Equal(original.Message, fromHost.Message);
        Assert.Equal(hostJson, System.Text.Json.JsonSerializer.Serialize(fromBridge, host));
    }
}
