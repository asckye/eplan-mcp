namespace EplanMcp.Catalog.Tests;

/// <summary>The string helpers the scanner and the extractor use on documentation IDs.</summary>
[Trait("Size", "Small")]
public sealed class SignatureHelperTests
{
    [Theory]
    [InlineData("System.String", false)]
    [InlineData("System.Collections.Generic.List{Eplan.EplApi.DataModel.Page}", false)]
    [InlineData("System.Int32[0:,0:]", false)]
    [InlineData("``0", false)]
    [InlineData("System.ValueType!Eplan.EplApi.Base.PointD", false)]
    [InlineData("EString*", true)]
    [InlineData("System.Double*", true)]
    [InlineData("DMFunctionHandle", true)]
    [InlineData("std.vector<BaseHandle,std.allocator<BaseHandle>>", true)]
    [InlineData(null, false)]
    public void IsNative_FlagsPointersAndTypesOutsideAnyNamespace(string? type, bool native) =>
        Assert.Equal(native, AssemblyScanner.IsNative(type));

    [Theory]
    [InlineData("M:Eplan.EplApi.HEServices.Export.Run(System.String,System.Int32)", "T:Eplan.EplApi.HEServices.Export")]
    [InlineData("P:Eplan.EplApi.DataModel.FunctionPropertyList.FUNC_TEXT", "T:Eplan.EplApi.DataModel.FunctionPropertyList")]
    [InlineData("M:Eplan.EplApi.DataModel.AnyPropertyId.op_Implicit(System.Int32)~Eplan.EplApi.DataModel.AnyPropertyId", "T:Eplan.EplApi.DataModel.AnyPropertyId")]
    [InlineData("M:CAnimationPoint.op_Implicit~CPoint", "T:CAnimationPoint")]
    [InlineData("T:Utilities", "")]
    public void DeclaringTypeId_CutsTheMemberNameAndSignature(string id, string expected) =>
        Assert.Equal(expected, Extractor.DeclaringTypeId(id));
}
