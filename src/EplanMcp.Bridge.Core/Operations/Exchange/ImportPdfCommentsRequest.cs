using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Both phases bind the same PDF metadata and existing Comment identities (SEC-31).</summary>
internal sealed class ImportPdfCommentsRequest(OpenProject project, ImportSource source, IReadOnlyList<string> ids)
{
    public OpenProject Project { get; } = project;

    public ImportSource Source { get; } = source;

    public IReadOnlyList<string> Ids { get; } = ids;

    public static ImportPdfCommentsRequest Check(ImportPdfCommentsArgs args, IProjectCatalog catalog, IPdfCommentsImporter importer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        ImportSource? source = ImportSource.Check(args.File, args.FilePath, args.Sha256, [".pdf"], violations);
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        source!.Read(hash: true, maxBytes: 100L * 1024 * 1024);
        return new ImportPdfCommentsRequest(project, source, importer.Identifiers(project.Path));
    }

    public string Fingerprint() => Operations.Fingerprint.Of(Ids.Select(id => "comment\t" + id).Append(Source.FingerprintLine));
}
