using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The part numbers of a parts management XML file as S24's export has them: <c>part</c> elements below the root with
/// <c>variant</c> children. Anything else is not such a file; a DTD is never processed. Real files.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartsXmlFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-partsxml-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ThePartNumbers_InFileOrder_WithTheirVariants()
    {
        string file = Write("""
            <?xml version="1.0" encoding="utf-8" ?>
            <partsmanagement count="3" database="C:\Parts\EPLAN_parts.alk">
             <part P_ARTICLE_PARTNR="SIE.3RH2122-1HB40" P_ARTICLE_TYPENR="3RH2122-1HB40">
              <variant P_ARTICLE_VARIANT="1"><functiontemplate /></variant>
              <variant P_ARTICLE_VARIANT="2" />
              <freeproperty />
             </part>
             <propertydefinition P_ARTICLE_PARTNR="not a part" />
             <part P_ARTICLE_PARTNR=" EPLANMCP.LARGE-TEST " />
             <part P_ARTICLE_PARTNR="SIE.3RH2122-1HB40"><variant P_ARTICLE_VARIANT="3" /></part>
            </partsmanagement>
            """);

        IReadOnlyList<FilePart> parts = PartsXmlFile.Read(file, "relays.xml");

        Assert.Equal([("SIE.3RH2122-1HB40", 3), ("EPLANMCP.LARGE-TEST", 1)], parts.Select(p => (p.PartNumber, p.Variants)));
    }

    [Theory]
    [InlineData("<parts><part P_ARTICLE_PARTNR=\"A\" /></parts>", "its root element is not <partsmanagement>")]
    [InlineData("<partsmanagement />", "it holds no parts")]
    [InlineData("<partsmanagement><propertydefinition /></partsmanagement>", "it holds no parts")]
    [InlineData("<partsmanagement><part P_ARTICLE_TYPENR=\"X\" /></partsmanagement>", "a <part> element has no P_ARTICLE_PARTNR")]
    [InlineData("<partsmanagement><part P_ARTICLE_PARTNR=\"A\"></partsmanagement>", "it is not well-formed XML (line 1")]
    [InlineData("PK\u0003\u0004 a zip", "it is not well-formed XML")]
    public void AnythingElse_IsAnInvalidArgument_NamingTheFileAsGiven(string content, string problem)
    {
        string file = Write(content);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => PartsXmlFile.Read(file, "supplier/relays.xml")).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'supplier/relays.xml' is not, as " + problem, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADocumentTypeDefinition_IsNeverProcessed()
    {
        string secret = Write("secret");
        string file = Write($"""
            <?xml version="1.0"?>
            <!DOCTYPE partsmanagement [<!ENTITY leak SYSTEM "file:///{secret.Replace('\\', '/')}">]>
            <partsmanagement><part P_ARTICLE_PARTNR="&leak;" /></partsmanagement>
            """);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => PartsXmlFile.Read(file, "x.xml")).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    private string Write(string content)
    {
        string file = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(file, content);
        return file;
    }
}
