using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace EplanMcp.Catalog.Tests;

/// <summary>The spelling rules of documentation IDs (ECMA-334 annex D) and the C++/CLI modifiers EPLAN's files keep.</summary>
[Trait("Size", "Small")]
public sealed class DocIdTypeProviderTests
{
    private static readonly DocIdTypeProvider _provider = DocIdTypeProvider.Instance;

    [Theory]
    [InlineData(PrimitiveTypeCode.Int32, "System.Int32")]
    [InlineData(PrimitiveTypeCode.String, "System.String")]
    [InlineData(PrimitiveTypeCode.Boolean, "System.Boolean")]
    [InlineData(PrimitiveTypeCode.Void, "System.Void")]
    [InlineData(PrimitiveTypeCode.IntPtr, "System.IntPtr")]
    [InlineData(PrimitiveTypeCode.Object, "System.Object")]
    public void GetPrimitiveType_SpellsTheSystemTypeName(PrimitiveTypeCode code, string expected) =>
        Assert.Equal(expected, _provider.GetPrimitiveType(code));

    [Fact]
    public void Arrays_ReferencesAndPointers_UseTheDocumentationSuffixes()
    {
        Assert.Equal("System.String[]", _provider.GetSZArrayType("System.String"));
        Assert.Equal("System.Int32[0:,0:]", _provider.GetArrayType("System.Int32", new ArrayShape(2, [], [])));
        Assert.Equal("System.String@", _provider.GetByReferenceType("System.String"));
        Assert.Equal("System.Double*", _provider.GetPointerType("System.Double"));
    }

    [Fact]
    public void GetGenericInstantiation_DropsTheArityAndListsTheArgumentsInBraces()
    {
        string type = _provider.GetGenericInstantiation(
            "System.Collections.Generic.Dictionary`2", ImmutableArray.Create("System.String", "System.Int32"));

        Assert.Equal("System.Collections.Generic.Dictionary{System.String,System.Int32}", type);
    }

    [Fact]
    public void GenericParameters_UseOneBacktickForTypesAndTwoForMethods()
    {
        Assert.Equal("`0", _provider.GetGenericTypeParameter(null, 0));
        Assert.Equal("``1", _provider.GetGenericMethodParameter(null, 1));
    }

    [Theory]
    [InlineData("System.Runtime.CompilerServices.IsConst", "System.String", "System.String")]
    [InlineData("System.Runtime.CompilerServices.IsLong", "System.Int32", "System.Int32")]
    [InlineData("System.Runtime.CompilerServices.IsBoxed", "System.ValueType!Eplan.EplApi.Base.PointD", "System.ValueType!Eplan.EplApi.Base.PointD")]
    [InlineData("Eplan.EplApi.Base.PointD", "System.ValueType", "System.ValueType!Eplan.EplApi.Base.PointD")]
    [InlineData("System.Runtime.CompilerServices.IsSignUnspecifiedByte", "System.SByte", "System.SByte!System.Runtime.CompilerServices.IsSignUnspecifiedByte")]
    [InlineData("System.Runtime.CompilerServices.IsImplicitlyDereferenced", "DMFunctionHandle*", "DMFunctionHandle")]
    public void GetModifiedType_OptionalModifiers_FollowTheIntelliSenseFiles(string modifier, string type, string expected) =>
        Assert.Equal(expected, _provider.GetModifiedType(modifier, type, isRequired: false));

    [Fact]
    public void GetModifiedType_RequiredModifier_IsDroppedAsRoslynDoes() =>
        Assert.Equal("System.Int32@", _provider.GetModifiedType("System.Runtime.InteropServices.InAttribute", "System.Int32@", isRequired: true));
}
