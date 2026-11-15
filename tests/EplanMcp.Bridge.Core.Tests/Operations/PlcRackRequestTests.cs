using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// plc.rack's checks and rack layout (docs/tools/eplan_plc_rack.md, sections 3, 4 and 7) against fake pages and a
/// card macro in a temporary macro directory; nothing is inserted.
/// </summary>
[Trait("Size", "Small")]
public sealed class PlcRackRequestTests : IDisposable
{
    private const string Rack = "=K1+UC1&EFS1/1";
    private const string Macro = "SIE/6ES7131-6BH01-0BA0.ema";

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-plc-rack-").FullName;
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Dryer", Path = @"D:\Scratch\Dryer", Current = true });
    private readonly FakePageReader _pages = new();
    private readonly FakeMacroReader _macros;
    private readonly FakeMacroInserter _inserter = new(new FakeDeviceReader());
    private readonly FakeDeviceRenamer _names = new();

    public PlcRackRequestTests()
    {
        string file = Path.Combine(_root, Macro);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "macro");
        _macros = new FakeMacroReader(_root)
        {
            PartSource = (part, variant) => new PartMacroSource { Part = part, PartVariant = variant ?? "1", Macro = part == "NOMACRO" ? string.Empty : Macro },
            ReadSelection = (representation, variant) => new MacroContent
            {
                LayoutVerified = true,
                Representations = [new("Overview", [0]), new("MultiLine", [1, 5, 6])],
                Representation = representation ?? "MultiLine",
                Variant = variant,
                Width = representation == "Overview" ? 80 : 24,
                Height = 60,
                ObjectCount = 4,
            },
        };
        Page(Rack, "Circuit", width: 420);
        Page("=K1+UC1&EFA1/1", "Overview");
        Page("=K1+UC1&EFA1/2", "Overview");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Pictures_LieSideBySideFromTheMacroWidths_AndOverviewsAreCheckedFirst()
    {
        PlcRackRequest request = Check(Args(Card("=K1+UC1-KE1", 1), Card("=K1+UC1-KE2", 2)));

        Assert.Equal([24d, 48d], request.Cards.Select(c => c.RackX));
        Assert.Equal(["=K1+UC1&EFA1/1", "=K1+UC1&EFA1/2"], request.Overviews.Select(r => r.Page.Name));
        Assert.All(request.Pictures, r => Assert.Equal((Rack, "MultiLine", 5), (r.Page.Name, r.Representation, r.Variant)));
        Assert.All(request.Overviews, r => Assert.Equal("Overview", r.Representation));
        Assert.Equal(2, request.Changes().Count);
        Assert.Equal(request.Fingerprint(), Check(Args(Card("=K1+UC1-KE1", 1), Card("=K1+UC1-KE2", 2))).Fingerprint());
    }

    [Fact]
    public void EveryShapeProblem_IsReportedAtOnce()
    {
        PlcRackCard bad = Card(" ", 1);
        bad.PictureVariant = null;
        PlcRackCard twin = Card("=K1+UC1-KE2", 1);
        PlcRackArgs args = Args(bad, twin, Card("=K1+UC1-KE2", 2));
        args.RackPage = null;
        args.Origin = [24];

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Check(args));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        foreach (string expected in new[] { "'rack_page'", "'origin'", "/cards/0/tag", "/cards/0/picture_variant", "Tag '=K1+UC1-KE2'", "Overview page '=K1+UC1&EFA1/1'" })
        { Assert.Contains(expected, error.Error.Message, StringComparison.Ordinal); }
    }

    [Fact]
    public void ATakenTag_AndAPageWithObjects_AreRefused()
    {
        _names.Functions.Add(("=K1+UC1-KE1", "1", true, Rack, "-KE1"));
        _inserter.Pages["=K1+UC1&EFA1/1"].Objects.Add("1/1/1/0");

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Check(Args(Card("=K1+UC1-KE1", 1))));

        Assert.Contains("Tag '=K1+UC1-KE1' is already used", error.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Page '=K1+UC1&EFA1/1' is not empty", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARackWiderThanThePage_IsRefused()
    {
        PlcRackCard[] cards = [.. Enumerable.Range(1, 2).Select(i => Card("=K1+UC1-KE" + i, i))];
        PlcRackArgs args = Args(cards);
        args.Origin = [400, 264];

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Check(args));

        Assert.Contains("beyond the page (420 mm)", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APartWithoutAWindowMacro_IsMacroNotFound()
    {
        PlcRackCard card = Card("=K1+UC1-KE1", 1);
        card.Part = "NOMACRO";

        Assert.Equal(ErrorReason.MacroNotFound, Assert.Throws<BridgeErrorException>(() => Check(Args(card))).Error.Reason);
    }

    private PlcRackRequest Check(PlcRackArgs args) => PlcRackRequest.Check(args, _catalog, _pages, _macros, _inserter, _names);

    private static PlcRackArgs Args(params PlcRackCard[] cards) => new() { Project = "Dryer", RackPage = Rack, Cards = [.. cards] };

    private static PlcRackCard Card(string tag, int overview) => new()
    {
        Tag = tag,
        Part = "SIE.6ES7131-6BH01-0BA0",
        OverviewPage = "=K1+UC1&EFA1/" + overview,
        PictureVariant = 5,
    };

    private void Page(string name, string type, double? width = null)
    {
        _inserter.Pages[name] = new MacroTargetPage { Name = name, Type = type, Width = width, Height = width is null ? null : 297 };
        _pages.Pages.Add(new PageSummary { Name = name });
    }
}
