using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PlacementGraphicsTests
{
    internal const string Graphics = """
        {"kind":"polyline","points":[[10,10],[20,15],[30,10]]},
        {"kind":"arc","center":[50,50],"radius_mm":5,"start_angle":30,"end_angle":120},
        {"kind":"path_text","position":[60,70],"height_mm":3.5,"contents":{"??_??":"Function"}},
        {"kind":"image","file":"graphics/test.png","corners":[[70,10],[80,20]]}
        """;

    [Theory]
    [InlineData("{\"kind\":\"polyline\"}", "/points")]
    [InlineData("{\"kind\":\"polyline\",\"points\":[]}", "/points")]
    [InlineData("{\"kind\":\"polyline\",\"points\":[[1,2]]}", "/points")]
    [InlineData("{\"kind\":\"polyline\",\"points\":[[1,2],[3,4,5]]}", "/points/1")]
    [InlineData("{\"kind\":\"polyline\",\"points\":[[1,2],[3,null]]}", "/points/1/1")]
    [InlineData("{\"kind\":\"arc\"}", "/center")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"start_angle\":0,\"end_angle\":90}", "/radius_mm")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":5,\"end_angle\":90}", "/start_angle")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":5,\"start_angle\":0}", "/end_angle")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":0,\"start_angle\":0,\"end_angle\":90}", "/radius_mm")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":-1,\"start_angle\":0,\"end_angle\":90}", "/radius_mm")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":1e400,\"start_angle\":0,\"end_angle\":90}", "/radius_mm")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":5,\"start_angle\":null,\"end_angle\":90}", "/start_angle")]
    [InlineData("{\"kind\":\"arc\",\"center\":[10,10],\"radius_mm\":5,\"start_angle\":0,\"end_angle\":1e400}", "/end_angle")]
    [InlineData("{\"kind\":\"path_text\"}", "/contents")]
    [InlineData("{\"kind\":\"path_text\",\"height_mm\":3,\"contents\":{\"??_??\":\"a\"}}", "/position")]
    [InlineData("{\"kind\":\"path_text\",\"position\":[10,10],\"contents\":{\"??_??\":\"a\"}}", "/height_mm")]
    [InlineData("{\"kind\":\"path_text\",\"position\":[10,10],\"height_mm\":0,\"contents\":{\"??_??\":\"a\"}}", "/height_mm")]
    [InlineData("{\"kind\":\"path_text\",\"position\":[10,10],\"height_mm\":3,\"contents\":{\"??_??\":\"\"}}", "/contents")]
    [InlineData("{\"kind\":\"image\",\"corners\":[[10,10],[20,20]]}", "/file")]
    [InlineData("{\"kind\":\"image\",\"file\":\"test.png\"}", "/corners")]
    [InlineData("{\"kind\":\"image\",\"file\":\"test.png\",\"corners\":[[10,10]]}", "/corners")]
    public void InvalidFields_ReportTheirPointer(string item, string path) =>
        Assert.Contains(PlacementCreatePlanning.Validate(PlacementCreateTests.Input(item)), p => p.Contains(path, StringComparison.Ordinal));

    [Theory]
    [InlineData("../test.png")]
    [InlineData("sub/../../test.png")]
    [InlineData("C:/test.png")]
    [InlineData("/test.png")]
    [InlineData("test.svg")]
    [InlineData("test.exe")]
    [InlineData("\\\\server\\share\\test.png")]
    [InlineData("sub\\..\\test.png")]
    [InlineData("test.png:stream")]
    public void ImageFiles_RejectEscapesAndOtherExtensions(string file)
    {
        string item = "{\"kind\":\"image\",\"file\":" + JsonSerializer.Serialize(file) + ",\"corners\":[[10,10],[20,20]]}";
        Assert.Contains(PlacementCreatePlanning.Validate(PlacementCreateTests.Input(item)), p => p.Contains("/file", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    [InlineData(".emf")]
    [InlineData(".wmf")]
    [InlineData(".PNG")]
    public void ImageFiles_AcceptOnlyWhitelistedImageExtensions(string extension) => Assert.Empty(PlacementCreatePlanning.Validate(
        PlacementCreateTests.Input("{\"kind\":\"image\",\"file\":\"graphics/test" + extension + "\",\"corners\":[[10,10],[20,20]]}")));

    [Fact]
    public void ArcAngles_AcceptFiniteDoublesBeyondDecimalRange() => Assert.Empty(PlacementCreatePlanning.Validate(
        PlacementCreateTests.Input("""{"kind":"arc","center":[50,50],"radius_mm":5,"start_angle":-1e100,"end_angle":1e100}""")));

    [Fact]
    public void RequestShape_PreservesEveryNewPrimitive()
    {
        JsonElement input = PlacementCreateTests.Input(Graphics);
        Assert.Empty(PlacementCreatePlanning.Validate(input));
        Assert.True(PropertyGetContractTests.Accepts(PlacementCreateContract.Input, input, PlacementCreateContract.Input));
        var rows = input.GetProperty("items").EnumerateArray().Select(PlacementCreatePlanning.Request).ToArray();
        Assert.Equal(["polyline", "arc", "path_text", "image"], rows.Select(p => p.Kind));
        Assert.Equal(3, rows[0].Points.Length);
        Assert.Equal([20d, 15d], rows[0].Points[1]);
        Assert.Equal([50d, 50d], rows[1].Position);
        Assert.Equal((5d, 30d, 120d), (rows[1].RadiusMm, rows[1].StartAngle, rows[1].EndAngle));
        Assert.Equal("Function", rows[2].Contents["??_??"]);
        Assert.Equal(3.5, rows[2].HeightMm);
        Assert.Equal("graphics/test.png", rows[3].File);
        Assert.Equal([80d, 20d], rows[3].EndPosition);
    }

    [Fact]
    public async Task Preview_UsesAllVerticesAndConservativeCircleBounds()
    {
        JsonElement result = PlacementCreateTests.Data(await PlacementCreateTests.Preview(PlacementCreateTests.Bridge(), PlacementCreateTests.Input(Graphics)));
        Assert.Equal([10d, 10d, 30d, 15d], result.GetProperty("items")[0].GetProperty("bounds").Deserialize<double[]>()!);
        Assert.Equal([45d, 45d, 55d, 55d], result.GetProperty("items")[1].GetProperty("bounds").Deserialize<double[]>()!);
        Assert.Equal(4, result.GetProperty("changes").GetArrayLength());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void NewKinds_PreserveDefaultAndExplicitDryRunSelection(bool? dryRun, bool preview)
    {
        JsonObject input = JsonNode.Parse(PlacementCreateTests.Input(Graphics).GetRawText())!.AsObject();
        if (dryRun.HasValue)
        { input["dry_run"] = dryRun.Value; }
        else
        { input.Remove("dry_run"); }
        JsonElement request = JsonSerializer.SerializeToElement(input);
        Assert.Empty(PlacementCreatePlanning.Validate(request));
        Assert.Equal(preview, WriteToolDefaults.IsPreview(request));
    }
}
