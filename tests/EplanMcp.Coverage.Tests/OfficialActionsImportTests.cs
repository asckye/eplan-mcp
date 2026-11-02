using System.Text.Json.Nodes;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Small")]
public sealed class OfficialActionPageTests
{
    internal static string Fixture(string name)
    {
        using var reader = new StreamReader(typeof(OfficialActionPageTests).Assembly.GetManifestResourceStream("EplanMcp.Coverage.Tests.fixtures.actions." + name + ".html")!);
        return reader.ReadToEnd();
    }

    [Fact]
    public void BackupTableHasLiteralEnumsFlagsPathsAndOptionalMarks()
    {
        OfficialActionPage page = OfficialActionPage.Parse("backup", Fixture("backup"));
        Assert.Equal(4, page.Parameters.Count);
        Assert.Equal("[\"PROJECT\",\"MASTERDATA\"]", Facts("TYPE")["values"]!.ToJsonString());
        Assert.Equal("[\"BACKUP\",\"SOURCEOUT\",\"ARCHIVE\"]", Facts("BACKUPMETHOD")["values"]!.ToJsonString());
        Assert.Equal("path", Facts("DESTINATIONPATH")["type"]!.GetValue<string>());
        Assert.True(Facts("DESTINATIONPATH")["required"]!.GetValue<bool>());
        JsonObject flag = Facts("AUTOCOPYREFDATA");
        Assert.Equal("flag", flag["type"]!.GetValue<string>());
        Assert.Equal("0", flag["default"]!.GetValue<string>());
        Assert.Equal("[\"0\",\"1\"]", flag["values"]!.ToJsonString());
        Assert.False(flag["required"]!.GetValue<bool>());
        JsonObject Facts(string name) => OfficialActionPage.Facts(page.Parameters[name], "PROJECT");
    }

    [Fact]
    public void ExportTableSupportsQuotedAndUnquotedEnumsAndNumericDefaults()
    {
        OfficialActionPage page = OfficialActionPage.Parse("export", Fixture("export"));
        JsonObject format = OfficialActionPage.Facts(page.Parameters["FORMAT"], "GRAPHICPROJECT");
        Assert.Equal("[\"TIF\",\"GIF\",\"PNG\",\"JPG\"]", format["values"]!.ToJsonString());
        Assert.Equal("TIF", format["default"]!.GetValue<string>());
        JsonObject depth = OfficialActionPage.Facts(page.Parameters["COLORDEPTH"], "GRAPHICPROJECT");
        Assert.Equal("integer", depth["type"]!.GetValue<string>());
        Assert.Equal("24", depth["default"]!.GetValue<string>());
        Assert.Equal("[\"1\",\"8\",\"16\",\"24\",\"32\"]", depth["values"]!.ToJsonString());
        Assert.Equal("NONE", OfficialActionPage.Facts(page.Parameters["IMAGECOMPRESSION"], null)["default"]!.GetValue<string>());
    }

    [Fact]
    public void RenumberTableSupportsAnnotatedDefaultsAndExplicitIntegerTypes()
    {
        OfficialActionPage page = OfficialActionPage.Parse("renumber", Fixture("renumber"));
        Assert.Equal("integer", OfficialActionPage.Facts(page.Parameters["STARTVALUE"], null)["type"]!.GetValue<string>());
        Assert.Equal("0", OfficialActionPage.Facts(page.Parameters["USESELECTION"], null)["default"]!.GetValue<string>());
        Assert.Equal("1", OfficialActionPage.Facts(page.Parameters["POSTNUMERATE"], null)["default"]!.GetValue<string>());
    }

    [Fact]
    public void OnlyExampleCommandsAreParsedWithQuotesEmptyValuesAndContinuations()
    {
        OfficialActionPage page = OfficialActionPage.Parse("backup", """
            <pre>backup /TYPE:NOT_AN_EXAMPLE</pre>
            <div id=exampleSectionContent><pre>
            backup /TYPE:PROJECT /DESTINATIONPATH:"C:\Example Output" /COMMENT:"Hello world"
            /AUTOCOPYREFDATA:0 /EMPTY:""
            backup /TYPE:MASTERDATA /FILENAME:*.fn1
            backup /TYPE:PROJECT /BROKEN:"unterminated
            </pre></div>
            """);
        Assert.Equal(2, page.Examples.Length);
        Assert.Equal("Hello world", page.Examples[0]["COMMENT"]!.GetValue<string>());
        Assert.Equal("C:\\Example Output", page.Examples[0]["DESTINATIONPATH"]!.GetValue<string>());
        Assert.Equal("0", page.Examples[0]["AUTOCOPYREFDATA"]!.GetValue<string>());
        Assert.Equal("", page.Examples[0]["EMPTY"]!.GetValue<string>());
        Assert.Equal("MASTERDATA", page.Examples[1]["TYPE"]!.GetValue<string>());
    }

    [Fact]
    public void ApplicabilityIsNotAValueAndDynamicDefaultsRemainUnknown()
    {
        JsonObject facts = OfficialActionPage.Facts("Default value: current setting. This parameter is only effective with TYPE: \"PROJECT\".", null);
        Assert.Null(facts["values"]);
        Assert.Null(facts["default"]);
        string description = "Is required for TYPE: \"PDFCOMMENTS\". Is optional for TYPE: \"DXFPAGE\".";
        Assert.True(OfficialActionPage.Facts(description, "PDFCOMMENTS")["required"]!.GetValue<bool>());
        Assert.False(OfficialActionPage.Facts(description, "DXFPAGE")["required"]!.GetValue<bool>());
    }

    [Fact]
    public void ExamplePrefixesCommentsAndExecutedCallingContextsAreData()
    {
        OfficialActionPage page = OfficialActionPage.Parse("ExampleAction", """
            <div id=exampleSectionContent><pre>
            EPLAN.exe /Variant:"Electric P8" ExampleAction /FILENAME:"C:\example.xml"
            ExampleAction /FILENAME:"D:\example.xml" // comment
            string action = "ExampleAction";
            context.AddParameter("FLAG", "false");
            action.Execute(context);
            other.AddParameter("NO", "ignored");
            </pre></div>
            """);
        Assert.Equal(3, page.Examples.Length);
        Assert.Equal("C:\\example.xml", page.Examples[0]["FILENAME"]!.GetValue<string>());
        Assert.Equal("D:\\example.xml", page.Examples[1]["FILENAME"]!.GetValue<string>());
        Assert.Equal("false", page.Examples[2]["FLAG"]!.GetValue<string>());
        Assert.Null(page.Examples[2]["NO"]);
    }

    [Fact]
    public void NumericLabelsAndApplicabilityNeverBecomeLiteralValues()
    {
        JsonObject numbers = OfficialActionPage.Facts("Possible values: 0 = \"Label zero\", 1 = \"Label one\"\n\n117 = other context", null);
        Assert.Equal("[\"0\",\"1\"]", numbers["values"]!.ToJsonString());
        Assert.Equal("flag", numbers["type"]!.GetValue<string>());
        Assert.Null(OfficialActionPage.Facts("Path and name.\n• \"PXFPROJECT\": Path and name of the file to be imported.", null)["values"]);
        string conditional = "TYPE: GRAPHIC (optional, 0 = color, 1 = black and white).\nThis is also used for \"PDF\" whereby\n• 0 = Color\n• 1 = Black and White\n• 2 = Grayscale";
        Assert.Equal("[\"0\",\"1\"]", OfficialActionPage.Facts(conditional, "GRAPHIC")["values"]!.ToJsonString());
        Assert.Equal("[\"0\",\"1\",\"2\"]", OfficialActionPage.Facts(conditional, "PDF")["values"]!.ToJsonString());
    }

    [Theory]
    [InlineData("Target file", "file")]
    [InlineData("Full name of an XML file", "file")]
    [InlineData("If TYPE is PDF, full path name", "path")]
    public void FileDestinationsAreRecognizedWithoutInventingFormats(string description, string expected) =>
        Assert.Equal(expected, OfficialActionPage.Facts(description, null)["type"]!.GetValue<string>());

    [Fact]
    public void FileFormatsRespectSystemTypesAndStandaloneConverterTokens()
    {
        string description = "File format (\"Text\",\"XML\")\nFor parts management (\"SystemImport\" and \"SystemExport\")";
        Assert.Equal("[\"Text\",\"XML\"]", OfficialActionPage.Facts(description, "EXPORT")["values"]!.ToJsonString());
        Assert.Equal("[\"SystemImport\",\"SystemExport\"]", OfficialActionPage.Facts(description, "IMPORTTOSYSTEM")["values"]!.ToJsonString());
        Assert.Contains("Edz", OfficialActionPage.Facts("File format (\"XML\"). \"Edz\" - export", null)["values"]!.AsArray().Select(n => n!.GetValue<string>()));
    }
}

[Trait("Size", "Medium")]
public sealed class OfficialActionsImportTests
{
    [Fact]
    public void ImportIsIdempotentMatchesTypesAndOnlyReportsUnmatchedActions()
    {
        using var repository = new TempRepository();
        repository.Write(OfficialActionsImport.RelativePath, """
            [{"id":"A:backup/PROJECT","action":"backup","type":"PROJECT","parameters":[{"name":"AUTOCOPYREFDATA","required":true}]},
             {"id":"A:backup/MASTERDATA","action":"backup","type":"MASTERDATA","parameters":[{"name":"DESTINATIONPATH"}]},
             {"id":"A:LocalOnly","action":"LocalOnly","type":null,"parameters":[]}]
            """);
        repository.Write("pages/_index.html", """
            <table><tr><td><a href="backup.html">backup</a></td><td></td></tr>
            <tr><td><a href="NewOfficial.html">NewOfficial</a></td><td></td></tr></table>
            """);
        repository.Write("pages/backup.html", OfficialActionPageTests.Fixture("backup") + """
            <div id=exampleSectionContent><pre>
            backup /TYPE:PROJECT /AUTOCOPYREFDATA:0
            backup /TYPE:MASTERDATA /DESTINATIONPATH:U:\temp
            </pre></div>
            """);
        string[] messages = OfficialActionsImport.Import(repository.Root, Path.Combine(repository.Root, "pages"));
        string first = repository.Read(OfficialActionsImport.RelativePath);
        Assert.Equal(messages, OfficialActionsImport.Import(repository.Root, Path.Combine(repository.Root, "pages")));
        Assert.Equal(first, repository.Read(OfficialActionsImport.RelativePath));
        JsonArray rows = JsonNode.Parse(first)!.AsArray();
        Assert.Equal(3, rows.Count);
        Assert.False(rows[0]!["parameters"]![0]!["required"]!.GetValue<bool>());
        Assert.Equal("0", rows[0]!["examples"]![0]!["AUTOCOPYREFDATA"]!.GetValue<string>());
        Assert.Null(rows[0]!["examples"]![0]!["DESTINATIONPATH"]);
        Assert.Equal("U:\\temp", rows[1]!["examples"]![0]!["DESTINATIONPATH"]!.GetValue<string>());
        Assert.Contains("Index actions without catalog rows: NewOfficial", messages);
        Assert.Contains("Catalog actions absent from index: LocalOnly", messages);
    }

    [Fact]
    public void MissingIndexWarnsAndLeavesCatalogBytesUntouched()
    {
        using var repository = new TempRepository();
        repository.Write(OfficialActionsImport.RelativePath, "[]");
        Assert.Contains(OfficialActionsImport.Import(repository.Root, repository.Root), m => m.StartsWith("warning:", StringComparison.Ordinal));
        Assert.Equal("[]", repository.Read(OfficialActionsImport.RelativePath));
    }
}
