namespace EplanMcp.Host.Packs;

/// <summary>Reads facts only from the prepared, read-only copy and explicit pages; never writes to that project.</summary>
internal interface IReferenceExtractor
{
    public IReadOnlyList<string> RequiredOperations { get; }
    public Task<ReferenceExtractionResult> ExtractAsync(string project, string resource, IReadOnlyList<string> pages, CancellationToken cancellationToken);
}
