using EplanMcp.Host.Tools;
using Microsoft.CodeAnalysis.CSharp;
namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class ApiTryArrayGeneratorTests
{
    private static readonly Dictionary<string, string[]> _enums = new() { ["Example.Enum"] = ["One", "Two"] };
    private static readonly Dictionary<string, string[]> _kinds = new() { ["Page"] = ["page"] };
    public static TheoryData<string> Shapes
    {
        get
        {
            var result = new TheoryData<string>();
            foreach (string element in new[] { "System.String", "System.Boolean", "System.Int32", "System.Int64", "System.Double", "Example.Enum", "Eplan.EplApi.Base.PointD", "Eplan.EplApi.DataModel.Page" })
            {
                result.Add(element + "[]");
                foreach (string collection in new[] { "IEnumerable", "List", "IList", "ICollection" })
                { result.Add("System.Collections.Generic." + collection + "{" + element + "}"); }
            }
            return result;
        }
    }
    private static ApiDetail Member(string type, string? direction = null) => new("overload", "M:Page.Test(" + type + ")", "Page.Test", Parameters: [new("values", type, direction)], ReturnType: "System.Void");
    private static ApiEntry Entry(ApiDetail member) => new(new(member.Id, "O:Page.Test", "method", member.Signature!, "registered", null, [], false), "Eplan.EplApi.DataModel", "T:Eplan.EplApi.DataModel.Page", "Test", null, [], [member]);

    [Theory, MemberData(nameof(Shapes))]
    public void EveryContainerAndElementIsAcceptedForInputsAndReturns(string type)
    {
        Assert.Null(ApiTryGenerator.ShapeExclusion(Member(type), _enums, _kinds));
        Assert.Null(ApiTryGenerator.ShapeExclusion(Member("System.Int32") with { ReturnType = type }, _enums, _kinds));
        string code = ApiTryGenerator.ArgumentValue(type, "parameters[\"values\"]", _enums, _kinds);
        Assert.Empty(CSharpSyntaxTree.ParseText("class C { object Call() => " + code + "; }", cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken));
        Assert.Contains(type.EndsWith("[]", StringComparison.Ordinal) ? "ToArray(" : "ToList(", code, StringComparison.Ordinal);
        if (!type.EndsWith("[]", StringComparison.Ordinal))
        { Assert.StartsWith("((global::" + type[..type.IndexOf('{', StringComparison.Ordinal)] + "<", code, StringComparison.Ordinal); }
    }
    [Theory]
    [InlineData("System.String[,]")]
    [InlineData("System.String[][]")]
    [InlineData("System.Collections.ArrayList")]
    [InlineData("System.Collections.Generic.Dictionary{System.String,System.String}")]
    [InlineData("System.Collections.Generic.List{System.String[]}")]
    [InlineData("Eplan.EplApi.Base.PointD3D[]")]
    [InlineData("Eplan.EplApi.DataModel.AnyPropertyId[]")]
    public void UnsupportedShapesStayExcluded(string type) => Assert.NotNull(ApiTryGenerator.ShapeExclusion(Member(type), _enums, _kinds));

    [Fact]
    public void OutIsOmittedFromSelection_RefScalarRemainsInput_AndBothReturnNamedValues()
    {
        ApiDetail output = Member("System.Int32@", "out");
        Assert.Equal("", ApiTryGenerator.Names(output));
        string code = ApiTryGenerator.MethodCase(Entry(output), output, _enums, _kinds, new Dictionary<string, ApiTryServices.Service>());
        Assert.Contains("out arg0", code, StringComparison.Ordinal);
        Assert.Contains("[\"values\"] = arg0", code, StringComparison.Ordinal);
        Assert.Contains("[\"returnValue\"] = null", code, StringComparison.Ordinal);
        Assert.Null(ApiTryGenerator.ShapeExclusion(Member("Eplan.EplApi.Base.PointD@"), _enums, _kinds));
        ApiDetail inputOutput = output with { Parameters = [new("values", "System.Int32@", "ref")] };
        Assert.Equal("values", ApiTryGenerator.Names(inputOutput));
        Assert.Contains("ref arg0", ApiTryGenerator.MethodCase(Entry(inputOutput), inputOutput, _enums, _kinds, new Dictionary<string, ApiTryServices.Service>()), StringComparison.Ordinal);
        foreach (string type in new[] { "Eplan.EplApi.DataModel.Page@", "System.String[]@" })
        { Assert.Equal("ref_out", ApiTryGenerator.ShapeExclusion(Member(type), _enums, _kinds)); }
    }
    [Fact]
    public void NativeByValueCollectionOverloadIsExcludedWithoutDroppingOrdinaryCopy()
    {
        const string Type = "Eplan.EplApi.DataModel.E3D.Placement3D";
        var kinds = new Dictionary<string, string[]> { ["E3D.Placement3D"] = ["placement3d"] };
        var member = new ApiDetail("overload", "M:" + Type + ".CopyTo(" + Type + ",System.Collections.Generic.List{" + Type + "})",
            Parameters: [new("newParent", Type), new("listObjects", "System.Collections.Generic.List{" + Type + "}")], ReturnType: "System.Collections.Generic.List{" + Type + "}", Static: true);
        Assert.Equal("unsupported_required_modifier", ApiTryGenerator.ShapeExclusion(member, _enums, kinds));
        Assert.Null(ApiTryGenerator.ShapeExclusion(member with { Id = "M:" + Type + ".CopyTo(" + Type + ")", Parameters = [new("newParent", Type)], ReturnType = Type, Static = false }, _enums, kinds));
    }

    [Fact]
    public void PointArraysAndRefPointsUseTheHandleAwareObjectResolver()
    {
        string code = ApiTryGenerator.ArgumentValue("Eplan.EplApi.Base.PointD[]", "parameters[\"points\"]", _enums, _kinds);
        Assert.Contains("resolve(item!, \"Eplan.EplApi.Base.PointD\")", code, StringComparison.Ordinal);
        Assert.Contains("StrongBox<global::Eplan.EplApi.Base.PointD>", code, StringComparison.Ordinal);
        ApiDetail member = Member("Eplan.EplApi.Base.PointD@", "ref");
        code = ApiTryGenerator.MethodCase(Entry(member), member, _enums, _kinds, new Dictionary<string, ApiTryServices.Service>());
        Assert.Contains("resolve(parameters[\"values\"]!", code, StringComparison.Ordinal);
        Assert.Contains("ref arg0", code, StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void ExplicitOutCollectionsAreSupportedWithoutRequiringInputs(string type)
    {
        ApiDetail output = Member(type + "@", "out");
        Assert.Null(ApiTryGenerator.ShapeExclusion(output, _enums, _kinds));
        Assert.Equal("", ApiTryGenerator.Names(output));
        Assert.Empty(CSharpSyntaxTree.ParseText("class C { object Call() { switch (\"\") { " + ApiTryGenerator.MethodCase(Entry(output), output, _enums, _kinds, new Dictionary<string, ApiTryServices.Service>()) + " } return null; } }", cancellationToken: TestContext.Current.CancellationToken).GetDiagnostics(TestContext.Current.CancellationToken));
    }
    [Fact]
    public void OutNameSetCollisionsStayAmbiguous_AndLifecycleAndResourcesStillWin()
    {
        ApiDetail a = Member("System.Int32@", "out"), b = Member("System.String@", "out");
        ApiEntry entry = Entry(a) with { Details = [a, b] };
        Assert.Empty(ApiTryGenerator.Methods(new([entry], []), _enums, _kinds));
        Assert.Equal("lifecycle_or_creation", ApiTryGenerator.Exclusion(Entry(a) with { Name = "Create" }, a, _enums, _kinds));
        ApiDetail paths = Member("System.String[]") with { Parameters = [new("files", "System.String[]")] };
        Assert.Equal("external_path_parameter", ApiTryServices.Exclusion(Entry(paths), paths));
        ApiDetail projects = paths with { Parameters = [new("projects", "System.String[]")] };
        Assert.Equal("external_path_parameter", ApiTryServices.Exclusion(Entry(projects), projects));
    }
    [Theory, MemberData(nameof(Shapes))]
    public void CollectionPropertyGettersAreSelectedWithTheSameElementRestrictions(string type)
    {
        ApiDetail property = new("overload", "P:Page.Values", "Page.Values", Parameters: [], ReturnType: type, Accessors: "get;set");
        ApiEntry entry = Entry(property) with { Row = Entry(property).Row with { Kind = "property" } };
        Assert.Single(ApiTryGenerator.Selected(new([entry], []), _enums, _kinds));
        Assert.False(ApiTryGenerator.PropertyReturn("System.Object[]", _enums, _kinds));
    }
}
