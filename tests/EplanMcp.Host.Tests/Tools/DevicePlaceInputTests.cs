using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class DevicePlaceInputTests
{
    [Theory]
    [InlineData(null, null, null, null, null, false)]
    [InlineData("P", null, null, null, null, true)]
    [InlineData("P", "2", null, null, null, true)]
    [InlineData(null, null, "IEC_symbol", "K", 0, true)]
    [InlineData("P", null, "IEC_symbol", "K", 0, false)]
    [InlineData(null, "2", "IEC_symbol", "K", 0, false)]
    [InlineData(null, null, "IEC_symbol", "K", null, false)]
    [InlineData(null, null, null, "K", 0, false)]
    [InlineData(null, null, "IEC_symbol", null, 0, false)]
    [InlineData(null, null, "IEC_symbol", "K", -1, false)]
    [InlineData(null, null, " ", "K", 0, false)]
    public void SourceGroups_AreCompleteAndExclusive(string? part, string? variant, string? library, string? symbol, int? number, bool valid)
    {
        var args = new DevicePlaceArgs { Project = "Line1", Part = part, Variant = variant, Library = library, Symbol = symbol, SymbolVariant = number };
        Assert.Equal(valid, DevicePlaceInputs.Check(args, null).Count == 0);
    }

    [Theory]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":4,\"offset_y_mm\":-4}", true)]
    [InlineData("{\"property_id\":20011,\"offset_x_mm\":0,\"offset_y_mm\":0}", true)]
    [InlineData("{\"property_id\":20027,\"offset_x_mm\":0,\"offset_y_mm\":0}", true)]
    [InlineData("{\"property_id\":20022,\"offset_x_mm\":4,\"offset_y_mm\":4}", false)]
    [InlineData("{\"property_id\":\"20214\",\"offset_x_mm\":4,\"offset_y_mm\":4}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":\"4\",\"offset_y_mm\":4}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":1e999,\"offset_y_mm\":4}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":4,\"offset_y_mm\":4,\"extra\":0}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":4,\"offset_x_mm\":5}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":4,\"offset_y_mm\":4,\"offset_y_mm\":5}", false)]
    [InlineData("{\"property_id\":20214,\"offset_x_mm\":4}", false)]
    [InlineData("null", false)]
    public void DisplayProperties_UseStrictMembersClosedIdsAndFiniteOffsets(string json, bool valid)
    {
        var args = Symbol();
        List<string> problems = DevicePlaceInputs.Check(args, [Json(json)]);
        Assert.Equal(valid, problems.Count == 0);
        if (valid)
        { Assert.Single(args.DisplayProperties!); }
        else
        { Assert.StartsWith("/display_properties/0:", Assert.Single(problems), StringComparison.Ordinal); }
    }

    [Fact]
    public void DisplayProperties_AcceptEightPlacementsAndRejectNine()
    {
        JsonElement item = Json("""{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}""");
        Assert.Empty(DevicePlaceInputs.Check(Symbol(), [.. Enumerable.Repeat(item, 8)]));
        Assert.Contains("at most 8", Assert.Single(DevicePlaceInputs.Check(Symbol(), [.. Enumerable.Repeat(item, 9)])), StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayProperties_AcceptEveryClosedId()
    {
        JsonElement[] display = [.. "20214,20213,20211,20215,20058,20011,20027".Split(',').Select(id => Json("{\"property_id\":" + id + ",\"offset_x_mm\":0,\"offset_y_mm\":0}"))];
        Assert.Empty(DevicePlaceInputs.Check(Symbol(), display));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Coordinates_RejectNonfiniteNumbersBeforeSerialization(double value)
    {
        var args = Symbol();
        args.X = value;
        Assert.StartsWith("/x:", Assert.Single(DevicePlaceInputs.Check(args, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void PartSource_RejectsSymbolOnlyOptions()
    {
        var args = new DevicePlaceArgs { Project = "Line1", Part = "P", ConnectionDesignations = ["1"] };
        Assert.Single(DevicePlaceInputs.Check(args, null));
        args.ConnectionDesignations = null;
        Assert.Single(DevicePlaceInputs.Check(args, []));
    }

    [Fact]
    public void ConnectionDesignations_AreBoundedPlainText()
    {
        foreach (string? value in new[] { null, "", " ", "A\n1", new string('a', 65) })
        {
            var args = Symbol();
            args.ConnectionDesignations = [value!];
            Assert.Contains("/connection_designations:", Assert.Single(DevicePlaceInputs.Check(args, null)), StringComparison.Ordinal);
        }
        var valid = Symbol();
        valid.ConnectionDesignations = [new string('a', 64)];
        Assert.Empty(DevicePlaceInputs.Check(valid, null));
    }

    [Fact]
    public async Task InvalidSource_IsRejectedBeforeAnyPipelineCall()
    {
        var result = await DeviceWriteTools.DevicePlaceAsync(null!, "Line1", "/1", 80, 160, "-K1", part: "P", library: "IEC_symbol", symbol: "K", symbol_variant: 0, cancellationToken: TestContext.Current.CancellationToken);
        JsonElement error = WriteToolHarness.Error(result);
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    private static DevicePlaceArgs Symbol() => new() { Project = "Line1", Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0 };
    private static JsonElement Json(string json) { using JsonDocument document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
}
