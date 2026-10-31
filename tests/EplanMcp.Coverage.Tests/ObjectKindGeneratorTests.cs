using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ObjectKindGeneratorTests
{
    private const string Prefix = "Eplan.EplApi.DataModel.";
    private static JsonElement Type(string name, string parent, string extra = "") => JsonSerializer.Deserialize<JsonElement>(
        "{\"id\":\"T:" + Prefix + name + "\",\"base\":\"" + Prefix + parent + "\"" + extra + "}");
    private static ApiEntry Member(string type, string name, string returns, ApiParameter[]? parameters = null, string? excluded = null, bool isStatic = false)
    {
        string id = (parameters is null ? "P:" : "M:") + Prefix + type + "." + name;
        return new(new(id, "O:" + Prefix + type + "." + name, parameters is null ? "property" : "method", name, "registered", null, [], false),
            Prefix, "T:" + Prefix + type, name, null, [], [new("overload", id, Parameters: parameters ?? [], ReturnType: returns, Accessors: parameters is null ? "get" : null, ExcludedReason: excluded, Static: isStatic)]);
    }

    [Fact]
    public void MappingUsesExactAndBaseFinderElements_AndRejectsExcludedOrNonNullShapes()
    {
        JsonElement[] types = [Type("StorableObject", "Object"), Type("Function", "StorableObject"), Type("EObjects.Terminal", "Function"),
            Type("AbstractDMObjectFilter", "Object"), Type("FunctionsFilter", "AbstractDMObjectFilter")];
        ApiEntry valid = Member("DMObjectsFinder", "GetFunctions", Prefix + "Function[]", [new("filter", Prefix + "FunctionsFilter")]);
        ApiIndexData data = new([Member("Function", "Properties", Prefix + "FunctionPropertyList"), valid,
            valid with { Name = "GetExcluded", Details = [valid.Details[0] with { ExcludedReason = "unsafe" }] },
            valid with { Name = "GetStatic", Details = [valid.Details[0] with { Static = true }] },
            valid with { Name = "GetScalar", Details = [valid.Details[0] with { Parameters = [new("index", "System.Int32")] }] },
            valid with { Name = "GetOther", Details = [valid.Details[0] with { ReturnType = Prefix + "Page[]" }] },
            valid with { Name = "GetGeneric", Details = [valid.Details[0] with { Id = "M:Generic``1" }] },
            valid with { Name = "GetWithCF" },
        ], []);
        ObjectKindGenerator.Kind terminal = Assert.Single(ObjectKindGenerator.Build(types, data), k => k.Name == "Terminal");
        Assert.Null(terminal.Reason);
        Assert.Equal(["finder.GetFunctions(null)"], terminal.Sources);
        Assert.Equal(Prefix + "Function", terminal.PropertyOwner);
        Assert.Contains("finder.GetFunctions(null)", Assert.Single(ObjectKindGenerator.Build(types, data), k => k.Name == "Function").Sources);
        Assert.Equal("no_safe_enumeration", Assert.Single(ObjectKindGenerator.Build(types, data with { Entries = [data.Entries[0]] }), k => k.Name == "Terminal").Reason);
    }

    [Fact]
    public void MostDerivedPropertiesAreMandatory_ExcludedOverrideNeverFallsBackToBase()
    {
        JsonElement[] types = [Type("StorableObject", "Object"), Type("Function", "StorableObject"), Type("EObjects.TerminalStrip", "Function"),
            Type("AbstractDMObjectFilter", "Object"), Type("FunctionsFilter", "AbstractDMObjectFilter")];
        ApiEntry derived = Member("EObjects.TerminalStrip", "Properties", Prefix + "EObjects.TerminalStripPropertyListComplete");
        ApiIndexData data = new([Member("Function", "Properties", Prefix + "FunctionPropertyList"), derived,
            Member("DMObjectsFinder", "GetFunctions", Prefix + "Function[]", [new("filter", Prefix + "FunctionsFilter")])], []);
        ObjectKindGenerator.Kind strip = Assert.Single(ObjectKindGenerator.Build(types, data), k => k.Name == "TerminalStrip");
        Assert.Equal(Prefix + "EObjects.TerminalStrip", strip.PropertyOwner);
        Assert.Null(strip.Reason);
        data = data with { Entries = [.. data.Entries.Select(e => e == derived ? e with { Row = e.Row with { ExcludedReason = "unsafe" } } : e)] };
        Assert.Equal("no_safe_properties", Assert.Single(ObjectKindGenerator.Build(types, data), k => k.Name == "TerminalStrip").Reason);
    }

    [Fact]
    public void UnsafeIdentitiesAndExcludedOrAbstractTypesRemainUnsupported()
    {
        JsonElement[] types = [Type("StorableObject", "Object"), Type("ArticleReference", "StorableObject"), Type("EObjects.TerminalAccessory", "ArticleReference"),
            Type("Graphics.PropertyPlacement", "StorableObject"), Type("ContextPropertyPlacement", "Graphics.PropertyPlacement"),
            Type("E3D.ContextPropertyPlacement3D", "ContextPropertyPlacement"), Type("Excluded", "StorableObject", ",\"excluded\":{}"),
            Type("Abstract", "StorableObject", ",\"modifiers\":\"abstract\"")];
        ObjectKindGenerator.Kind[] kinds = ObjectKindGenerator.Build(types, new([], []));
        Assert.Equal(2, kinds.Count(k => k.Reason == "transient_reference:A60"));
        Assert.Equal(3, kinds.Count(k => k.Reason == "shared_parent_identity:A57"));
        Assert.Equal("catalog_excluded", Assert.Single(kinds, k => k.Name == "Excluded").Reason);
        Assert.Equal("abstract_type", Assert.Single(kinds, k => k.Name == "Abstract").Reason);
    }

    [Fact]
    public void DocumentedFinderExceptionsDoNotClaimAnEnumerationPath()
    {
        JsonElement[] types = [Type("StorableObject", "Object"), Type("Placement", "StorableObject"), Type("Page", "Placement"),
            Type("MasterData.Symbol", "StorableObject"), Type("E3D.Placement3D", "StorableObject"), Type("E3D.Function3D", "E3D.Placement3D"),
            Type("E3D.InstallationSpace", "E3D.Function3D"), Type("E3D.MateEntry", "E3D.Placement3D"),
            Type("AbstractDMObjectFilter", "Object"), Type("Filter", "AbstractDMObjectFilter")];
        ApiIndexData data = new([Member("StorableObject", "Properties", Prefix + "StorableObjectPropertyList"),
            Member("DMObjectsFinder", "GetStorableObjects", Prefix + "StorableObject[]", [new("filter", Prefix + "Filter")]),
            Member("DMObjectsFinder", "GetFunctions3D", Prefix + "E3D.Function3D[]", [new("filter", Prefix + "Filter")]),
            Member("DMObjectsFinder", "GetPlacements3D", Prefix + "E3D.Placement3D[]", [new("filter", Prefix + "Filter")]),
            Member("Project", "InstallationSpaces", Prefix + "E3D.InstallationSpace[]"),
        ], []);
        ObjectKindGenerator.Kind[] kinds = ObjectKindGenerator.Build(types, data);
        Assert.Equal("no_safe_enumeration", Assert.Single(kinds, k => k.Name == "Page").Reason);
        Assert.Equal("no_safe_enumeration", Assert.Single(kinds, k => k.Name == "Symbol").Reason);
        ObjectKindGenerator.Kind space = Assert.Single(kinds, k => k.Name == "InstallationSpace");
        Assert.Contains("project.InstallationSpaces", space.Sources);
        Assert.DoesNotContain("finder.GetFunctions3D(null)", space.Sources);
        Assert.DoesNotContain("finder.GetPlacements3D(null)", Assert.Single(kinds, k => k.Name == "MateEntry").Sources);
    }

    [Theory]
    [InlineData("E3D.InstallationSpace", true)]
    [InlineData("Graphics.Arc", true)]
    [InlineData("Planning.PCTLoop", true)]
    [InlineData("Revisions.Comparison", true)]
    [InlineData("Graphics.PropertyPlacement", false)]
    [InlineData("ArticleReference", false)]
    public void LocatorParameterTypesCoverSupportedKindsAndRefuseUnsafeIdentities(string type, bool supported)
    {
        Assert.Equal(supported, ApiTryCatalog.ValidValue([Prefix + type], "locator", ""));
        Assert.False(ApiTryCatalog.ValidValue([Prefix + type], "String", ""));
        Assert.False(ApiTryCatalog.ValidValue([Prefix + type], "Object", ""));
    }
}

[Trait("Size", "Medium")]
public sealed class ObjectKindCatalogTests
{
    private static string Root => Program.RepositoryRoot(AppContext.BaseDirectory)!;
    [Fact]
    public void CatalogMappingCoversEveryPublicStorableTypeAndEveryPublishedDictionary()
    {
        ObjectKindGenerator.Kind[] kinds = ObjectKindGenerator.Load(Root);
        Assert.Equal(ObjectReadTypes.Types.Order(StringComparer.Ordinal), kinds.Select(k => k.Name));
        Assert.All(kinds, k => Assert.Equal(ObjectReadTypes.Supported(k.Name), k.Reason is null));
        Assert.All(kinds.Where(k => k.Reason is null && k.Name is not ("BlackBox" or "PLCTerminal")), k => Assert.NotEmpty(k.Sources));
        Assert.Contains("finder.GetPlacements(null)", Assert.Single(kinds, k => k.Name == "Arc").Sources);
        Assert.Contains("finder.GetPlanningSegments(null)", Assert.Single(kinds, k => k.Name == "PCTLoop").Sources);
        Assert.Contains("finder.GetConnections(null)", Assert.Single(kinds, k => k.Name == "RoutedConnection").Sources);
        Assert.Contains("finder.GetPlacements3D(null)", Assert.Single(kinds, k => k.Name == "MountingPanel").Sources);
        Assert.Contains("project.InstallationSpaces", Assert.Single(kinds, k => k.Name == "InstallationSpace").Sources);
        Assert.Contains("project.InstallationSpaces.SelectMany(s => s.Connection3Ds)", Assert.Single(kinds, k => k.Name == "Connection3D").Sources);
        Assert.All(kinds.Where(k => k.Name.StartsWith("Comparison", StringComparison.Ordinal)), k => Assert.Contains("finder.GetStorableObjects(null)", k.Sources));
    }

    [Fact]
    public void DispatchHonorsCatalogAndBannedMembersAndPreservesDerivedPropertyAccess()
    {
        string dispatch = File.ReadAllText(Path.Combine(Root, ApiTryGenerator.DispatchPath));
        ApiIndexData data = ApiIndexGenerator.Data(Root);
        foreach (ApiEntry entry in data.Entries)
        {
            foreach (ApiDetail member in entry.Details.Where(d => d.Kind == "overload" && (entry.Row.ExcludedReason is not null || d.ExcludedReason is not null)))
            { Assert.DoesNotContain(JsonSerializer.Serialize(member.Signature), dispatch, StringComparison.Ordinal); }
        }
        Assert.DoesNotContain(".LoadFromMasterdata(", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain(".LockAllObjects(", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("DataModel.ViewPart)target).UseLocalPropertyPlacements =", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("DataModel.Page)target).Location", dispatch, StringComparison.Ordinal);
        string objectDispatch = File.ReadAllText(Path.Combine(Root, ObjectKindGenerator.DispatchPath));
        Assert.Contains("((global::Eplan.EplApi.DataModel.EObjects.TerminalStrip)obj).Properties", objectDispatch, StringComparison.Ordinal);
        Assert.Contains("((global::Eplan.EplApi.DataModel.EObjects.PlugStrip)obj).Properties", objectDispatch, StringComparison.Ordinal);
        Assert.DoesNotContain(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(objectDispatch, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>(), m => m.Name.Identifier.ValueText == "Location");
        Assert.DoesNotContain("GetAll", objectDispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("WithCF", objectDispatch, StringComparison.Ordinal);
    }
}
