using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PartListOperationTests
{
    private const string Path1 = @"D:\Projects\Line1";
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = Path1, Current = true };
    private static readonly OpenProject _line2 = new() { Name = "Line2", Path = @"D:\Projects\Line2" };

    private readonly FakePartReader _parts = new();

    [Fact]
    public void Execute_SumsUpTheReferencesOfEachPartAndVariant()
    {
        _parts.Parts.References.AddRange(
        [
            OnFunction("SIE.3RT2016-1BB41", "=A1-K1", count: 1),
            OnFunction("SIE.3RT2016-1BB41", "=A1-K2", count: 2),
            OnFunction("SIE.3RT2016-1BB41", "=A1-K1", count: 1),
            OnFunction("SIE.3RT2016-1BB41", "=A1-K3", variant: "2"),
            OnConnection("14AWG-BLK"),
            OnConnection("14AWG-BLK"),
            OnFunction("14AWG-BLK", "=A1-W1"),
        ]);

        ListData<PartListItem> data = Run(new PartListArgs { Limit = 50 });

        Assert.Equal("Line1", data.Project);
        Assert.Equal(3, data.Total);
        Assert.Equal(
            [("14AWG-BLK", "1", 3, 1, 2), ("SIE.3RT2016-1BB41", "1", 4, 2, 0), ("SIE.3RT2016-1BB41", "2", 1, 1, 0)],
            data.Items.Select(p => (p.PartNumber, p.Variant, p.Quantity, p.Devices, p.Connections)));
        Assert.Equal((Path1, (string?)null), Assert.Single(_parts.Reads));
    }

    [Fact]
    public void Execute_FunctionsWithoutATag_CountOneDeviceEachAndOtherOwnersOnlyInTheQuantity()
    {
        _parts.Parts.References.AddRange(
        [
            OnFunction("RIT.8601080", string.Empty),
            OnFunction("RIT.8601080", string.Empty),
            new PartReferenceRecord { PartNumber = "RIT.8601080", Variant = "1", Count = 5, Owner = PartOwner.Other },
        ]);

        PartListItem part = Assert.Single(Run(new PartListArgs { Limit = 50 }).Items);

        Assert.Equal((7, 2, 0), (part.Quantity, part.Devices, part.Connections));
    }

    [Fact]
    public void Execute_DescribesEachPartFromThePartDataOfItsVariantOrElseAnother()
    {
        _parts.Parts.References.AddRange([OnFunction("PXC.3031212", "-X1", variant: "2"), OnFunction("PXC.3031212", "-X2", variant: "3"), OnFunction("EPL.1", "-Q1")]);
        _parts.Parts.Articles.AddRange(
        [
            new ArticleRecord { PartNumber = "PXC.3031212", Variant = "1", Description = "端子", Manufacturer = "PXC" },
            new ArticleRecord { PartNumber = "PXC.3031212", Variant = "3", Description = "端子 3", Manufacturer = "PXC" },
        ]);

        ListData<PartListItem> data = Run(new PartListArgs { Limit = 50 });

        Assert.Equal(
            [("EPL.1", (string?)null, (string?)null), ("PXC.3031212", "端子", "PXC"), ("PXC.3031212", "端子 3", "PXC")],
            data.Items.Select(p => (p.PartNumber, p.Description, p.Manufacturer)));
    }

    [Fact]
    public void Execute_SortsByPartNumberOrdinallyThenByVariant_AndAnEmptyVariantIsNull()
    {
        _parts.Parts.References.AddRange(
        [
            OnFunction("b.2", "-K1"),
            OnFunction("B.1", "-K2", variant: "2"),
            OnFunction("B.1", "-K3", variant: string.Empty),
            OnFunction("A.9", "-K4"),
            OnFunction(string.Empty, "-K5"),
        ]);

        ListData<PartListItem> data = Run(new PartListArgs { Limit = 50 });

        Assert.Equal([("A.9", "1"), ("B.1", (string?)null), ("B.1", "2"), ("b.2", "1")], data.Items.Select(p => (p.PartNumber, p.Variant)));
    }

    [Theory]
    [InlineData("sie", null, new[] { "SIE.3RT2016-1BB41", "SIE.3SU1100" })]
    [InlineData(null, "3rt", new[] { "SIE.3RT2016-1BB41" })]
    [InlineData(null, "接触器", new[] { "SIE.3RT2016-1BB41" })]
    [InlineData(null, "Taster", new[] { "SIE.3SU1100" })]
    [InlineData("pxc", "接触器", new string[0])]
    [InlineData(" ", "", new[] { "PXC.3031212", "SIE.3RT2016-1BB41", "SIE.3SU1100", "XYZ.1" })]
    public void Execute_ManufacturerAndText_FilterIgnoringCaseAndCombineWithAnd(string? manufacturer, string? text, string[] expected)
    {
        _parts.Parts.References.AddRange([OnFunction("SIE.3RT2016-1BB41", "-K1"), OnFunction("SIE.3SU1100", "-S1"), OnFunction("PXC.3031212", "-X1"), OnFunction("XYZ.1", "-Y1")]);
        _parts.Parts.Articles.AddRange(
        [
            new ArticleRecord { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Description = "接触器", Manufacturer = "SIE" },
            new ArticleRecord { PartNumber = "SIE.3SU1100", Variant = "1", Description = "Drucktaster", Manufacturer = "SIE" },
            new ArticleRecord { PartNumber = "PXC.3031212", Variant = "1", Description = "Terminal", Manufacturer = "PXC" },
        ]);

        ListData<PartListItem> data = Run(new PartListArgs { Manufacturer = manufacturer, Text = text, Limit = 50 });

        Assert.Equal(expected, data.Items.Select(p => p.PartNumber));
    }

    [Fact]
    public void Execute_Language_IsPassedToTheReaderTrimmedAndAnUnknownOneIsInvalid()
    {
        Run(new PartListArgs { Language = " en_US ", Limit = 50 });

        BridgeError error = Fails(new PartListArgs { Language = "xx_XX", Limit = 50 });

        Assert.Equal("en_US", _parts.Reads[0].Language);
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch()
    {
        _parts.Parts.References.AddRange(Enumerable.Range(1, 7).Select(i => OnFunction($"P.{i}", $"-K{i}")));

        ListData<PartListItem> data = Run(new PartListArgs { Offset = 2, Limit = 3 });

        Assert.Equal(["P.3", "P.4", "P.5"], data.Items.Select(p => p.PartNumber));
        Assert.Equal(7, data.Total);
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsInvalidArgument()
    {
        _parts.Parts.References.Add(OnFunction("P.1", "-K1"));

        Assert.Equal(ErrorReason.InvalidArgument, Fails(new PartListArgs { Offset = 5, Limit = 5 }).Reason);
    }

    [Fact]
    public void Execute_NamedProject_ReadsThatProjectsPath()
    {
        Run(new PartListArgs { Project = "line2", Limit = 50 });

        Assert.Equal(_line2.Path, Assert.Single(_parts.Reads).Path);
    }

    [Fact]
    public void Execute_UnknownProject_IsProjectNotOpenBeforeAnythingIsRead()
    {
        Assert.Equal(ErrorReason.ProjectNotOpen, Fails(new PartListArgs { Project = "Line9", Limit = 50 }).Reason);
        Assert.Empty(_parts.Reads);
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _parts.Parts.References.AddRange([OnFunction("SIE.3RT2016-1BB41", "-K1"), OnConnection("14AWG-BLK")]);
        _parts.Parts.Articles.Add(new ArticleRecord { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Manufacturer = "SIE" });
        var router = new OperationRouter([new PartListOperation(new FakeCatalog(_line1), _parts)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.PartList, JObject.Parse("""{"manufacturer":"SIE","language":"zh_CN","offset":0,"limit":10}"""), "c");

        Assert.Equal(1, (int)data["total"]!);
        JToken item = data["items"]![0]!;
        Assert.Equal(("SIE.3RT2016-1BB41", 1, 1), ((string?)item["partNumber"], (int)item["quantity"]!, (int)item["devices"]!));
        Assert.Equal("zh_CN", Assert.Single(_parts.Reads).Language);
    }

    private static PartReferenceRecord OnFunction(string partNumber, string tag, string variant = "1", int count = 1) =>
        new() { PartNumber = partNumber, Variant = variant, Count = count, Owner = PartOwner.Function, OwnerName = tag };

    private static PartReferenceRecord OnConnection(string partNumber) =>
        new() { PartNumber = partNumber, Variant = "1", Count = 1, Owner = PartOwner.Connection };

    private ListData<PartListItem> Run(PartListArgs args) =>
        (ListData<PartListItem>)new PartListOperation(new FakeCatalog(_line1, _line2), _parts).Execute(args)!;

    private BridgeError Fails(PartListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;
}
