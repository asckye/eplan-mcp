namespace EplanMcp.Host.Packs;

/// <summary>Used until M3 supplies an extractor; records the missing read capability without inventing facts.</summary>
internal sealed class GapOnlyReferenceExtractor : IReferenceExtractor
{
    public IReadOnlyList<string> RequiredOperations => [];
    public Task<ReferenceExtractionResult> ExtractAsync(string project, string resource, IReadOnlyList<string> pages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReferenceExtractionResult.Missing(resource, "reference.extract", "Reference fact extraction is unavailable; install an extractor before trying again."));
    }
}
