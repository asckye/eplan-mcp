using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_part_build decoding, validation, field comparison and export-compatible XML, entirely in memory.</summary>
[Trait("Size", "Small")]
[Trait("Tool", "eplan_part_build")]
public sealed class PartBuildTests
{
    [Fact]
    public void Csv_QuotedNewlinesEscapesAndBlankLines_PreservePhysicalRowNumbers()
    {
        List<PartBuildTable.Row> table = Csv("part_number,description.en_US\n\nA,\"A, \"\"relay\"\"\ntext\"\nB,Second");
        Assert.Equal([1, 3, 5], table.Select(r => r.Number));
        Assert.Equal("A, \"relay\"\ntext", table[1].Cells[1]);
    }

    [Fact]
    public void Csv_HeaderDelimitersInsideQuotes_AndCrLineEndings_AreHandled()
    {
        List<PartBuildTable.Row> rows = Csv("\"Number;part;label\",manufacturer\rA,ACME\rB,OTHER");
        Assert.Equal([1, 2, 3], rows.Select(r => r.Number));
        Assert.Equal(["Number;part;label", "manufacturer"], rows[0].Cells);
        Assert.Equal(["A", "ACME"], rows[1].Cells);
    }

    [Fact]
    public void Csv_LeadingBlankLines_DoNotChangeDelimiterDetectionOrRowNumbers()
    {
        List<PartBuildTable.Row> rows = Csv("\n  \npart_number;manufacturer\nA;ACME\n\nB;OTHER\n");
        Assert.Equal([3, 4, 6], rows.Select(r => r.Number));
        Assert.Equal(["A", "ACME"], rows[1].Cells);
    }

    [Fact]
    public void Mapping_MustNotAssignOneSourceColumnToTwoFields()
    {
        Assert.Throws<InvalidDataException>(() => Preview("part_number,Label\nA,ACME", mapping: new()
        {
            ["manufacturer"] = "Label",
            ["supplier"] = "Label",
        }));
    }

    [Fact]
    public void NumericDiffs_CompareValues_WithoutChangingTextIdentifiers()
    {
        PartBuildResult result = Preview("part_number,width,product_group,type_number\nA,2.50,03,001", [new()
        {
            PartNumber = "A", BuildValues = new() { ["width"] = "2.5", ["product_group"] = "3", ["type_number"] = "1" },
        }]);
        PartBuildResult.Row row = Assert.Single(result.Rows);
        Assert.Equal("update", row.Action);
        Assert.Equal("type_number", Assert.Single(row.Diff).Key);
    }

    [Theory]
    [InlineData("part_number\n\"A\nB\"")]
    [InlineData("part_number,function_category,connection_points\nA,200,\"A1\nA2\"")]
    public void IdentifierControlCharacters_AreRowErrors(string csv)
    {
        Assert.Equal("error", Assert.Single(Preview(csv).Rows).Action);
    }

    [Fact]
    public void MappingAndUnknownColumns_AreReportedAlongsideFieldDiffs()
    {
        PartBuildResult result = Preview("Number;Maker;width;ignored\nA;NEW;2.5;untrusted\nB;NEW;0;x", [new PartSearchItem { PartNumber = "A", BuildValues = new() { ["manufacturer"] = "OLD", ["width"] = "2.5" } }], new() { ["part_number"] = "Number", ["manufacturer"] = "Maker" });
        Assert.Equal(["ignored"], result.UnknownColumns);
        Assert.Equal(["update", "create"], result.Rows.Select(r => r.Action));
        Assert.Equal(["OLD", "NEW"], result.Rows[0].Diff["manufacturer"]);
        Assert.DoesNotContain("width", result.Rows[0].Diff.Keys);
    }

    [Theory]
    [InlineData("manufacturer\nX")]
    [InlineData("part_number,part_number\nA,B")]
    [InlineData("part_number,\nA,B")]
    public void InvalidHeaders_AreRejectedBeforeComparison(string csv) => Assert.Throws<InvalidDataException>(() => Preview(csv));

    [Theory]
    [InlineData("width", "-1")]
    [InlineData("width", "NaN")]
    [InlineData("height", "1e5")]
    [InlineData("depth", "Infinity")]
    [InlineData("product_group", "relay")]
    [InlineData("product_subgroup", "1.5")]
    [InlineData("function_category", "0")]
    [InlineData("description.en_US", "x@y")]
    [InlineData("description.en_US", "x;y")]
    public void InvalidValues_AreRowErrors(string field, string value)
    {
        PartBuildResult.Row row = Assert.Single(Preview($"part_number,{field}\nA,{value}").Rows);
        Assert.Equal("error", row.Action);
        Assert.NotEmpty(row.Errors);
    }

    [Fact]
    public void RequiredNumbersAndDuplicates_AllAffectedRowsAreErrors()
    {
        PartBuildResult result = Preview("part_number,manufacturer\n,ACME\nA,ACME\nA,Other\nB,ACME");
        Assert.Equal(["error", "error", "error", "create"], result.Rows.Select(r => r.Action));
        Assert.Equal([2, 3, 4, 5], result.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void MultipleVariantsAndMultipleTemplates_AreRejected()
    {
        PartBuildResult result = Preview("part_number,manufacturer\nA,ACME\nB,ACME", [new() { PartNumber = "A", Variant = "2" }, new() { PartNumber = "B", BuildValues = new() { ["function_category"] = "multiple" } }]);
        Assert.All(result.Rows, r => Assert.Equal("error", r.Action));
    }

    [Fact]
    public void PreservingAnUnrepresentableExistingDescription_RejectsTheRow()
    {
        PartBuildResult result = Preview("part_number,manufacturer\nA,ACME", [new() { PartNumber = "A", BuildValues = new() { ["description.en_US"] = "x@y" } }]);
        Assert.Equal("error", Assert.Single(result.Rows).Action);
    }

    [Fact]
    public void NoOp_BlankCellsAndOmittedColumns_PreserveExistingValues()
    {
        PartBuildResult result = Preview("part_number,manufacturer,type_number\nA,ACME,", [new() { PartNumber = "A", BuildValues = new() { ["manufacturer"] = "ACME", ["type_number"] = "KEEP" } }]);
        Assert.Equal("skip", Assert.Single(result.Rows).Action);
        Assert.Empty(result.Rows[0].Diff);
    }

    [Fact]
    public void Xml_FollowsTheS24ExportAndExistingImportFixtureShape()
    {
        // Attribute names and nesting come from S24's exported partsmanagement/part/variant/functiontemplate.
        var values = new Dictionary<string, string>
        {
            ["part_number"] = "FICTION.RELAY",
            ["manufacturer"] = "FICTION",
            ["type_number"] = "R1",
            ["description.en_US"] = "Relay & coil",
            ["description.zh_CN"] = "继电器",
            ["technical_data"] = "DC 24V",
            ["product_group"] = "3",
            ["product_subgroup"] = "1",
            ["width"] = "2.5",
            ["function_category"] = "200",
            ["connection_points"] = "A1|A2",
            ["macro_file"] = "imports/relay.ema",
            ["picture_file"] = "imports/relay.png",
        };
        XDocument actual = PartBuildResult.Xml([values]);
        XElement root = actual.Root!;
        Assert.Equal(("partsmanagement", "1", "EPLAN.PartsManagement"), (root.Name.LocalName, (string?)root.Attribute("count"), (string?)root.Attribute("type")));
        XElement part = Assert.Single(root.Elements("part"));
        Assert.Equal("FICTION.RELAY", (string?)part.Attribute("P_ARTICLE_PARTNR"));
        Assert.Equal("en_US@Relay & coil;zh_CN@继电器;", (string?)part.Attribute("P_ARTICLE_DESCR1"));
        Assert.Null(part.Attribute("P_ARTICLE_CHARACTERISTICS"));
        XElement variant = Assert.Single(part.Elements("variant"));
        Assert.Equal("1", (string?)variant.Attribute("P_ARTICLE_VARIANT"));
        Assert.Equal("DC 24V", (string?)variant.Attribute("P_ARTICLE_CHARACTERISTICS"));
        XElement template = Assert.Single(variant.Elements("functiontemplate"));
        Assert.Equal(("200", "A1\nA2", "1"), ((string?)template.Attribute("functiondefcategory"), (string?)template.Attribute("connectionDesignation"), (string?)template.Attribute("pos")));
        XDocument exportedShape = XDocument.Parse("""
            <partsmanagement type="EPLAN.PartsManagement" count="1">
             <part P_ARTICLE_PARTTYPE="1" P_ARTICLE_PARTNR="FICTION.RELAY" P_ARTICLE_MANUFACTURER="FICTION" P_ARTICLE_TYPENR="R1"
                   P_ARTICLE_DESCR1="en_US@Relay &amp; coil;zh_CN@继电器;" P_ARTICLE_PRODUCTGROUP="3" P_ARTICLE_PRODUCTSUBGROUP="1"
                   P_ARTICLE_WIDTH="2.5" P_ARTICLE_MACRO="imports/relay.ema" P_ARTICLE_PICTUREFILE="imports/relay.png">
              <variant P_ARTICLE_VARIANT="1" P_ARTICLE_CHARACTERISTICS="DC 24V">
               <functiontemplate functiondefcategory="200" connectionDesignation="A1&#xA;A2" pos="1" />
              </variant>
             </part>
            </partsmanagement>
            """);
        static XElement Ordered(XElement element) => new(element.Name, element.Attributes().OrderBy(a => a.Name.LocalName, StringComparer.Ordinal), element.Elements().Select(Ordered));
        Assert.Equal(Ordered(exportedShape.Root!).ToString(), Ordered(actual.Root!).ToString());
    }

    [Fact]
    public void Xml_UpdatesPreserveExistingPartType()
    {
        XDocument xml = PartBuildResult.Xml([new() { ["part_number"] = "A", ["part_type"] = "2" }]);
        Assert.Equal("2", (string?)xml.Root!.Element("part")!.Attribute("P_ARTICLE_PARTTYPE"));
    }

    [Fact]
    public void Xlsx_UsesFirstSheetRelationshipSharedAndInlineStrings_AndSparsePositions()
    {
        using MemoryStream zip = Workbook("""
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="C1" t="inlineStr"><is><t>width</t></is></c></row>
            <row r="4"><c r="A4" t="inlineStr"><is><t>0001</t></is></c><c r="C4"><v>2.5</v></c></row>
            """);
        List<PartBuildTable.Row> rows = PartBuildTable.Read(zip, true);
        Assert.Equal([1, 4], rows.Select(r => r.Number));
        Assert.Equal(["part_number", "", "width"], rows[0].Cells);
        Assert.Equal(["0001", "", "2.5"], rows[1].Cells);
    }

    [Theory]
    [InlineData("<row r='1'><c r='A1'><f>1+1</f><v>2</v></c></row>", false)]
    [InlineData("<row r='1'><c r='A1' t='e'><v>#N/A</v></c></row>", false)]
    [InlineData("<row r='1'><c r='BM1'><v>1</v></c></row>", false)]
    [InlineData("<row><c r='A1'><v>1</v></c></row>", false)]
    [InlineData("<row r='1'><c r='A2'><v>1</v></c></row>", false)]
    [InlineData("<row r='1'><c r='A1'><v>1</v></c></row>", true)]
    public void UnsafeXlsx_IsRejected(string rows, bool external)
    {
        using MemoryStream zip = Workbook(rows, external);
        Assert.Throws<InvalidDataException>(() => PartBuildTable.Read(zip, true));
    }

    [Fact]
    public void OversizedTables_AreBoundedInMemory()
    {
        Assert.Throws<InvalidDataException>(() => Csv("part_number\n" + string.Join('\n', Enumerable.Range(0, 51))));
        Assert.Throws<InvalidDataException>(() => Csv("part_number\n" + new string('x', 501)));
    }

    internal static List<PartBuildTable.Row> Csv(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return PartBuildTable.Read(stream, false);
    }

    private static PartBuildResult Preview(string csv, List<PartSearchItem>? parts = null, Dictionary<string, string>? mapping = null)
    {
        var result = new PartBuildResult();
        result.Compare(Csv(csv), mapping, parts ?? [], []);
        return result;
    }

    private static MemoryStream Workbook(string rows, bool external = false)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add("xl/workbook.xml", "<workbook xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='First' r:id='r1'/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", $"<Relationships><Relationship Id='r1' Target='worksheets/custom.xml'{(external ? " TargetMode='External'" : "")}/></Relationships>");
            Add("xl/worksheets/custom.xml", "<worksheet><sheetData>" + rows + "</sheetData></worksheet>");
            Add("xl/sharedStrings.xml", "<sst><si><t>part_number</t></si></sst>");
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
