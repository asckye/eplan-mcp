using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The parts of eplan_macro_insert that need no EPLAN and no file (docs/tools/eplan_macro_insert.md, section 7): the
/// outline against the page, the device parts a merge would take, the devices listed and the change's text.
/// </summary>
[Trait("Size", "Small")]
public sealed class MacroInsertRequestTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Batch_RequiresOneToTwentyItems(int count)
    {
        var args = new MacroInsertArgs { Project = "EES1", Items = [.. Enumerable.Range(0, count).Select(_ => new MacroInsertItem())] };

        Assert.Contains(MacroInsertRequest.BatchProblems(args), problem => problem.StartsWith("/items:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("page", "\"=MA1+UC1&EFS1/1\"")]
    [InlineData("macro", "\"Motor.ema\"")]
    [InlineData("part", "\"PXC.2861344\"")]
    [InlineData("partVariant", "\"1\"")]
    [InlineData("representation", "\"MultiLine\"")]
    [InlineData("x", "0")]
    [InlineData("y", "0")]
    [InlineData("variant", "0")]
    [InlineData("numbering", "\"number\"")]
    [InlineData("macroBox", "true")]
    [InlineData("values", "{\"#<Function_text>\":\"Forward control\"}")]
    public void Batch_RefusesEverySuppliedSingleItemField(string field, string value)
    {
        var json = JObject.Parse("{\"project\":\"EES1\",\"items\":[{}],\"" + field + "\":" + value + "}");
        MacroInsertArgs args = json.ToObject<MacroInsertArgs>(BridgeJson.Serializer)!;
        string expected = field == "partVariant" ? "part_variant" : field == "macroBox" ? "macro_box" : field;

        Assert.Contains(MacroInsertRequest.BatchProblems(args), problem => problem.StartsWith("/" + expected + ":", StringComparison.Ordinal));
    }

    [Fact]
    public void Batch_Ees1DriveInputKeepsThreeOrderedMacroPlacementsAndValueSets()
    {
        const string json = """
            {
              "project": "EES1",
              "items": [
                {"page":"=MA1+UC1&EFS1/1","macro":"EPLAN_Macro/203_Electrical_Engine/101_01_Variant_1/Reverse_Contactor_Control.ema","x":4,"y":288},
                {"page":"=MA1+UC1&EFS1/2","macro":"EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema","x":24,"y":276,"values":{"#<Function_text>":"Forward control"}},
                {"page":"=MA1+UC1&EFS1/2","macro":"EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema","x":76,"y":276,"values":{"#<Function_text>":"Reverse control"}}
              ]
            }
            """;
        MacroInsertArgs args = JObject.Parse(json).ToObject<MacroInsertArgs>(BridgeJson.Serializer)!;

        Assert.Empty(MacroInsertRequest.BatchProblems(args));
        Assert.Equal([4d, 24d, 76d], args.Items!.Select(item => item.X));
        Assert.Equal("=MA1+UC1&EFS1/1", args.Items![0].Page);
        Assert.Equal("Forward control", args.Items[1].Values!["#<Function_text>"]);
        Assert.Equal("Reverse control", args.Items[2].Values!["#<Function_text>"]);
    }

    [Fact]
    public void Values_SelectiveModuleChannelAcceptsAnExactValueSet()
    {
        var content = new MacroContent
        {
            Placeholders = [new() { Name = "#<EES_F_CH1>", ValueSets = ["3A", "6A", "10A"], Variables = ["Current"] }],
        };

        Assert.Empty(MacroInsertRequest.ValueProblems(content, new Dictionary<string, string> { ["#<EES_F_CH1>"] = "6A" }));
        Assert.Empty(MacroInsertRequest.ValueProblems(content, null));
    }

    [Theory]
    [InlineData("#<UNKNOWN>")]
    [InlineData("#<ees_f_ch1>")]
    public void Values_UnknownOrDifferentlyCasedNameListsAvailablePlaceholders(string name)
    {
        var content = new MacroContent { Placeholders = [new() { Name = "#<EES_F_CH1>", ValueSets = ["6A"] }] };

        string problem = Assert.Single(MacroInsertRequest.ValueProblems(content, new Dictionary<string, string> { [name] = "6A" }));

        Assert.StartsWith("/values/" + name + ":", problem, StringComparison.Ordinal);
        Assert.Contains("Available placeholders: #<EES_F_CH1>", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5A")]
    [InlineData("6a")]
    public void Values_UnknownOrDifferentlyCasedRecordListsAvailableValueSets(string valueSet)
    {
        var content = new MacroContent { Placeholders = [new() { Name = "#<EES_F_CH1>", ValueSets = ["3A", "6A", "10A"] }] };

        string problem = Assert.Single(MacroInsertRequest.ValueProblems(content, new Dictionary<string, string> { ["#<EES_F_CH1>"] = valueSet }));

        Assert.StartsWith("/values/#<EES_F_CH1>:", problem, StringComparison.Ordinal);
        Assert.Contains("3A, 6A, 10A", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_DuplicateNameIsAmbiguousEvenWhenOneHolderHasTheValueSet()
    {
        var content = new MacroContent
        {
            Placeholders =
            [
                new() { Name = "#<EES_F_CH1>", ValueSets = ["6A"] },
                new() { Name = "#<EES_F_CH1>", ValueSets = ["10A"] },
            ],
        };

        Assert.Contains("exactly one placeholder", Assert.Single(MacroInsertRequest.ValueProblems(content,
            new Dictionary<string, string> { ["#<EES_F_CH1>"] = "6A" })), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Values_RequireBetweenOneAndTwentyEntries(int count)
    {
        var values = Enumerable.Range(0, count).ToDictionary(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "6A");

        Assert.Equal("/values: Supply between 1 and 20 placeholder value sets.", Assert.Single(MacroInsertRequest.ValueProblems(new(), values)));
    }

    [Fact]
    public void Values_EscapeTheJsonPointerAndLimitAvailableNamesAndRecordsToTwenty()
    {
        var content = new MacroContent
        {
            Placeholders = [.. Enumerable.Range(0, 21).Select(i => new MacroPlaceholderRecord
            {
                Name = "Channel" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ValueSets = [.. Enumerable.Range(0, 21).Select(record => record.ToString(System.Globalization.CultureInfo.InvariantCulture) + "A")],
            })],
        };
        string nameProblem = Assert.Single(MacroInsertRequest.ValueProblems(content, new Dictionary<string, string> { ["a/b~c"] = "6A" }));
        string recordProblem = Assert.Single(MacroInsertRequest.ValueProblems(content, new Dictionary<string, string> { ["Channel0"] = "unknown" }));

        Assert.StartsWith("/values/a~1b~0c:", nameProblem, StringComparison.Ordinal);
        Assert.Contains("Channel19", nameProblem, StringComparison.Ordinal);
        Assert.DoesNotContain("Channel20", nameProblem, StringComparison.Ordinal);
        Assert.Contains("19A", recordProblem, StringComparison.Ordinal);
        Assert.DoesNotContain("20A", recordProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void Pxc2861344_OverviewVariantZero_OnOverviewPage_AcceptsTheLiveGroupSymbolMacro()
    {
        var args = new EplanMcp.Contracts.MacroInsertArgs { Part = "PXC.2861344", Representation = "Overview", Variant = 0 };
        var observed = new MacroContent { Representations = [new("MultiLine", [0]), new("Overview", [0])] };

        Assert.Empty(MacroInsertRequest.RepresentationProblems(new() { Type = "Overview" }, observed, args.Representation, args.Variant ?? 0));
    }

    [Theory]
    [InlineData("MultiLine", "Overview")]
    [InlineData("Overview", "Circuit")]
    public void Representation_MustMatchPageType(string representation, string pageType)
    {
        var content = new MacroContent { Representations = [new(representation, [0])] };
        Assert.Contains(MacroInsertRequest.RepresentationProblems(new() { Type = pageType }, content, representation, 0), p => p.Contains("'page'", StringComparison.Ordinal));
    }

    [Fact]
    public void Pxc2861250_WithoutMultiLine_ReportsAvailableOverviewRepresentation()
    {
        var content = new MacroContent { Representations = [new("Overview", [0])] };
        string problem = Assert.Single(MacroInsertRequest.RepresentationProblems(new() { Type = "Circuit" }, content, "MultiLine", 0));
        Assert.Contains("available: Overview", problem, StringComparison.Ordinal);
    }

    // S10's macro, 52 x 268 mm, on S10's sample page, 420 x 297.02 mm.
    private const double Width = 52;
    private const double Height = 268;
    private const double PageWidth = 420;
    private const double PageHeight = 297.02;

    [Theory]
    [InlineData(0, 268)]
    [InlineData(368, 297.02)]
    [InlineData(40, 280)]
    public void Outside_AnOutlineWithinThePage_IsNull(double x, double y)
    {
        Assert.Null(MacroInsertRequest.Outside(x, y, Width, Height, PageWidth, PageHeight));
    }

    [Theory]
    [InlineData(368.5, 280)] // past the right edge
    [InlineData(40, 297.5)] // above the top
    [InlineData(40, 200)] // S10's point: the lower part below the page
    public void Outside_AnOutlinePastAnEdge_GivesTheSizesAndTheRanges(double x, double y)
    {
        string? problem = MacroInsertRequest.Outside(x, y, Width, Height, PageWidth, PageHeight);

        Assert.Equal(
            "'x' and 'y' put the macro's outline, 52 x 268 mm below and right of its top left corner at (x, y), outside the page, 420 x 297.02 mm: x must be from 0 to 368 and y from 268 to 297.02.",
            problem);
    }

    [Fact]
    public void Outside_AMacroLargerThanThePage_SaysSo()
    {
        Assert.Equal("the macro, 52 x 268 mm, is larger than the page, 210 x 148.5 mm.", MacroInsertRequest.Outside(0, 100, Width, Height, 210, 148.5));
    }

    [Theory]
    [InlineData("+-XG8:?", "XG8")]
    [InlineData("=+A1-QA1", "QA1")]
    [InlineData("=GAA+A1-?QA1", "?QA1")]
    [InlineData("=EC1+A3-EC1-MA2", "MA2")]
    [InlineData("+A1-XG9:?1", "XG9")]
    [InlineData("=K1+A1:1.4", null)]
    [InlineData("+-", null)]
    [InlineData("", null)]
    public void DevicePart_IsTheTextAfterTheLastDashBeforeAColon(string tag, string? part)
    {
        Assert.Equal(part, MacroInsertRequest.DevicePart(tag));
    }

    [Fact]
    public void Devices_LeaveOutWires_AndListEachTagOnceWithItsShownTag()
    {
        MacroFunctionRecord[] functions =
        [
            new() { Name = "+A1-XG5:?", Category = "Terminal" },
            new() { Name = "+A1-XG5:?", VisibleName = "-XG5", Category = "Terminal" },
            new() { Name = "+-WG6", VisibleName = ">-WG6", Category = "Cable", Main = true },
            new() { Name = "+-WG6", Category = "Wire" },
            new() { Name = string.Empty, Category = "Terminal" },
            new() { Name = "=+A1-QA1", VisibleName = "-QA1", Category = "NoContact" },
        ];

        Assert.Equal(
            [("+A1-XG5:?", "-XG5", "Terminal"), ("+-WG6", "-WG6", "Cable"), ("=+A1-QA1", "-QA1", "NoContact")],
            MacroInsertRequest.Devices(functions).Select(d => (d.Tag, d.VisibleTag, d.Category)));
    }

    [Theory]
    [InlineData(0, true, 8, "Contactor_Feedback_Control.ema at (40, 200), numbering number, 8 devices")]
    [InlineData(2, false, 1, "Contactor_Feedback_Control.ema at (40, 200), variant 2, numbering keep, 1 device")]
    public void Describe_IsTheChangeTheToolSpecShows(int variant, bool number, int devices, string text)
    {
        Assert.Equal(text, MacroInsertRequest.Describe("EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema", 40, 200, variant, number, devices));
    }

    [Fact]
    public void Merged_IsATagWithAMainFunctionBeforeAndMoreAfter()
    {
        // S10: numbering None inserted the macro's cable again, and the project had +A1-WG6 twice.
        string[] before = ["+A1-WG6", "=GAA+A1-KF1", "=GAA+A1-KF2", "=GAA+A1-KF2", string.Empty];
        string[] after = ["+A1-WG6", "+A1-WG6", "=GAA+A1-KF1", "=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-WG7", "+A1-WG7", string.Empty, string.Empty];

        Assert.Equal(["+A1-WG6"], MacroInsertRequest.Merged(before, after));
    }

    [Fact]
    public void Merged_ADeviceThatHadTwoMainFunctionsAndGetsAThird_IsMerged_AndTagsAreSorted()
    {
        string[] before = ["=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-XG5"];
        string[] after = ["=GAA+A1-KF2", "=GAA+A1-KF2", "=GAA+A1-KF2", "+A1-XG5", "+A1-XG5"];

        Assert.Equal(["+A1-XG5", "=GAA+A1-KF2"], MacroInsertRequest.Merged(before, after));
    }

    [Fact]
    public void Merged_TagsDifferingInCase_AreNotTheSameTag()
    {
        Assert.Empty(MacroInsertRequest.Merged(["+A1-WG6"], ["+A1-WG6", "+a1-wg6"]));
    }

    [Fact]
    public void Describe_KeepsTwoDecimalsOfThePoint()
    {
        Assert.Equal("Top.ema at (40.13, 0.5), numbering number, 0 devices", MacroInsertRequest.Describe("Top.ema", 40.126, 0.5, 0, number: true, 0));
    }
}
