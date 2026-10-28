using EplanMcp.Contracts;
using EplanMcp.Host.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class ReferenceExtractorTests
{
    [Fact]
    public async Task GapOnly_RecordsMissingCapabilityAndNoFactsOrLocalPaths()
    {
        var extractor = new GapOnlyReferenceExtractor();
        ReferenceExtractionResult result = await extractor.ExtractAsync(@"C:\scratch\Reference.elk", "references/Sample.elk", ["=AP+A1/1"], TestContext.Current.CancellationToken);
        Assert.Empty(extractor.RequiredOperations);
        Assert.Empty(result.Facts);
        ScanFinding gap = Assert.Single(result.Gaps);
        Assert.Equal(FindingSeverity.Gap, gap.Severity);
        Assert.Equal("references/Sample.elk", gap.Subject);
        Assert.Equal(ErrorReason.CapabilityMissing, result.Problem!.Reason);
        Assert.Equal("reference.extract", result.Problem.Metadata!["missing"]);
        Assert.Contains("install", result.Problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", result.Problem.Message, StringComparison.Ordinal);
        Assert.Null(result.Problem.Execution);
    }

    [Fact]
    public async Task GapOnly_Cancelled_ProducesNoEvidence()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GapOnlyReferenceExtractor().ExtractAsync("copy", "resource", ["1"], cancel.Token));
    }
}
