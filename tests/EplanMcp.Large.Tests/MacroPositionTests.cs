using System.Text.Json;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class MacroPositionTests(LiveEplan eplan)
{
    [Theory]
    [InlineData("PXC/2861344.ema", "Overview", 0, 292)]
    [InlineData("PXC/2701290.ema", "MultiLine", 16, 168)]
    public async Task SelectedVariantReturnsReferencePointAndFunctionLocations(string path, string representation, double x, double y)
    {
        var request = new Dictionary<string, object?>
        {
            ["project"] = eplan.ProjectPath,
            ["macro"] = path,
            ["representation"] = representation,
            ["variant"] = 0
        };
        TestContext.Current.TestOutputHelper!.WriteLine("request: " + JsonSerializer.Serialize(request));
        JsonElement result = await eplan.CallAsync("eplan_macro_get", request);
        TestContext.Current.TestOutputHelper.WriteLine("readback: " + result.GetRawText());
        JsonElement macro = result.GetProperty("macro");
        double[] reference = [.. macro.GetProperty("reference_point").EnumerateArray().Select(n => n.GetDouble())];
        Assert.Equal([x, y], reference);
        Assert.Contains(macro.GetProperty("representations").EnumerateArray(), r => r.GetProperty("type").GetString() == representation
            && r.GetProperty("variants").EnumerateArray().Any(v => v.GetInt32() == 0));
        Assert.Contains(macro.GetProperty("devices").EnumerateArray(), d => d.GetProperty("location").ValueKind == JsonValueKind.Array
            && d.GetProperty("location").GetArrayLength() == 2);
        foreach (JsonElement device in macro.GetProperty("devices").EnumerateArray())
        {
            double[]? location = device.GetProperty("location").ValueKind == JsonValueKind.Array
                ? [.. device.GetProperty("location").EnumerateArray().Select(n => n.GetDouble())] : null;
            TestContext.Current.TestOutputHelper.WriteLine("coordinate evidence: " + JsonSerializer.Serialize(new
            {
                tag = device.GetProperty("tag").GetString(),
                reference_point = reference,
                location,
                location_minus_reference = location is null ? null : new[] { location[0] - reference[0], location[1] - reference[1] }
            }));
        }
    }
}
