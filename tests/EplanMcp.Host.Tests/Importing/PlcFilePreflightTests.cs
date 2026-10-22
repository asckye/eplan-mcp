using System.Text;
using System.Xml;
using EplanMcp.Host.Importing;

namespace EplanMcp.Host.Tests.Importing;

[Trait("Size", "Small")]
public sealed class PlcFilePreflightTests
{
    [Theory]
    [InlineData("<Unknown xmlns='urn:unverified'><DeviceName>=F+A2-KE1</DeviceName></Unknown>")]
    [InlineData("<Root xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'><Part ref='#internal'/></Root>")]
    public async Task UnknownVocabularyAndInternalReferences_AreSafeWithoutClaimingNameExtraction(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        await PlcFilePreflight.ValidateAsync(stream, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<Root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY x 'value'>]><root>&x;</root>")]
    [InlineData("<!DOCTYPE root SYSTEM 'https://example.invalid/test.dtd'><root/>")]
    [InlineData("<root xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='file:///secret'/></root>")]
    [InlineData("<root xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:noNamespaceSchemaLocation='https://example.invalid/schema'/>")]
    public async Task MalformedXmlDtdAndExternalXmlReferences_AreRefused(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        await Assert.ThrowsAsync<XmlException>(() => PlcFilePreflight.ValidateAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledPreflight_StopsBeforeCompleting()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<root><child/></root>"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlcFilePreflight.ValidateAsync(stream, cancellation.Token));
    }
}
