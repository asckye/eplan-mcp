namespace EplanMcp.Catalog.Tests;

/// <summary>Counting layers, overload groups and the mechanical exclusion rules (design section 3).</summary>
[Trait("Size", "Small")]
public sealed class ClassifierTests
{
    private const string FunctionList = "T:Eplan.EplApi.DataModel.FunctionPropertyList";
    private const string PointD = "T:Eplan.EplApi.Base.PointD";

    [Fact]
    public void Classify_PropertyIdOfAPropertyList_IsDataDictionaryOfItsObject()
    {
        CatalogEntry plain = Classified("P:Eplan.EplApi.DataModel.FunctionPropertyList.FUNC_TEXT", "property", FunctionList, "FUNC_TEXT");
        CatalogEntry indexed = Classified("P:Eplan.EplApi.DataModel.FunctionPropertyList.FUNC_TEXT(System.Int32)", "property",
            FunctionList, "FUNC_TEXT", parameters: [new("index", "System.Int32")]);

        Assert.All([plain, indexed], e =>
        {
            Assert.Equal(Classifier.DataDictionary, e.Layer);
            Assert.Equal("Function", e.Object);
            Assert.Null(e.Group);
        });
    }

    [Fact]
    public void Classify_GenericAccessorOfAPropertyList_IsAnOperation()
    {
        CatalogEntry entry = Classified("P:Eplan.EplApi.DataModel.FunctionPropertyList.Property(Eplan.EplApi.DataModel.AnyPropertyId)",
            "property", FunctionList, "Property", parameters: [new("id", "Eplan.EplApi.DataModel.AnyPropertyId")]);

        Assert.Equal(Classifier.Operation, entry.Layer);
        Assert.Equal("O:Eplan.EplApi.DataModel.FunctionPropertyList.Property", entry.Group);
    }

    [Fact]
    public void Classify_EnumValueNestedInAPropertiesClass_IsDataDictionary_OtherEnumValuesAreTypeLayer()
    {
        CatalogEntry id = Classified("F:Eplan.EplApi.DataModel.Properties.Page.PAGE_TYPE", "enum_value",
            "T:Eplan.EplApi.DataModel.Properties.Page", "PAGE_TYPE");
        CatalogEntry other = Classified("F:Eplan.EplApi.Base.Unit.Millimeter", "enum_value", "T:Eplan.EplApi.Base.Unit", "Millimeter");

        Assert.Equal((Classifier.DataDictionary, "Page"), (id.Layer, id.Object));
        Assert.Equal((Classifier.TypeLayer, null), (other.Layer, other.Object));
    }

    [Theory]
    [InlineData("method", Classifier.Operation)]
    [InlineData("property", Classifier.Operation)]
    [InlineData("operator", Classifier.Operation)]
    [InlineData("constructor", Classifier.TypeLayer)]
    [InlineData("lifecycle", Classifier.TypeLayer)]
    [InlineData("field", Classifier.TypeLayer)]
    [InlineData("event", Classifier.TypeLayer)]
    public void Classify_PutsEachKindInItsLayer(string kind, string layer) =>
        Assert.Equal(layer, Classified("M:Eplan.EplApi.HEServices.Export.Run", kind, "T:Eplan.EplApi.HEServices.Export", "Run").Layer);

    [Fact]
    public void Classify_Overloads_ShareOneGroup()
    {
        CatalogEntry one = Classified("M:Eplan.EplApi.HEServices.Renumber.Pages(System.Int32)", "method",
            "T:Eplan.EplApi.HEServices.Renumber", "Pages", parameters: [new("start", "System.Int32")]);
        CatalogEntry two = Classified("M:Eplan.EplApi.HEServices.Renumber.Pages", "method", "T:Eplan.EplApi.HEServices.Renumber", "Pages");

        Assert.Equal("O:Eplan.EplApi.HEServices.Renumber.Pages", one.Group);
        Assert.Equal(one.Group, two.Group);
    }

    [Fact]
    public void Classify_MechanicalRules_ApplyTheFirstRuleThatMatches()
    {
        const string type = "T:Eplan.EplApi.Base.Internal.X";
        const string internalNs = "Eplan.EplApi.Base.Internal";

        Assert.Equal(new Exclusion("internal", "native_signature"),
            Classified("M:Eplan.EplApi.Base.Internal.X.Y(EString*)", "method", type, "Y", internalNs, obsolete: true, native: true).Excluded);
        Assert.Equal(new Exclusion("internal", "internal_namespace"),
            Classified("M:Eplan.EplApi.Base.Internal.X.Z", "method", type, "Z", internalNs, obsolete: true).Excluded);
        Assert.Equal(new Exclusion("deprecated", "obsolete_attribute"),
            Classified("M:Eplan.EplApi.Base.PointD.Old", "method", PointD, "Old", obsolete: true).Excluded);
    }

    [Fact]
    public void Classify_ObjectOverridesOperatorsAndDelegateMembers_AreNotApplicable()
    {
        Assert.Equal("object_override", Classified("M:Eplan.EplApi.Base.PointD.ToString", "method", PointD, "ToString").Excluded?.Rule);
        Assert.Equal("object_override", Classified("M:Eplan.EplApi.Base.PointD.Equals(System.Object)", "method", PointD, "Equals",
            parameters: [new("obj", "System.Object")]).Excluded?.Rule);
        Assert.Equal("operator", Classified("M:Eplan.EplApi.Base.PointD.op_Equality", "operator", PointD, "op_Equality").Excluded?.Rule);
        Assert.Equal("delegate_member",
            Classified("M:Eplan.EplApi.Base.Handler.Invoke", "delegate_method", "T:Eplan.EplApi.Base.Handler", "Invoke").Excluded?.Rule);
    }

    [Fact]
    public void Classify_TypedEqualsAndOrdinaryMethods_StayInTheDenominator()
    {
        Assert.Null(Classified("M:Eplan.EplApi.Base.PointD.Equals(Eplan.EplApi.Base.PointD)", "method", PointD, "Equals",
            parameters: [new("other", "Eplan.EplApi.Base.PointD")]).Excluded);
        Assert.Null(Classified("M:Eplan.EplApi.HEServices.Translate.TranslateObjects", "method",
            "T:Eplan.EplApi.HEServices.Translate", "TranslateObjects").Excluded);
    }

    private static CatalogEntry Classified(
        string id, string kind, string declaringType, string name, string ns = "Eplan.EplApi.DataModel",
        IReadOnlyList<Parameter>? parameters = null, bool obsolete = false, bool native = false)
    {
        var entry = new CatalogEntry
        {
            Id = id,
            Kind = kind,
            Assembly = "Eplan.EplApi.Test",
            Namespace = ns,
            Name = name,
            DeclaringType = declaringType,
            Parameters = parameters,
            Obsolete = obsolete,
            NativeSignature = native,
        };
        Classifier.Classify(entry);
        return entry;
    }
}
