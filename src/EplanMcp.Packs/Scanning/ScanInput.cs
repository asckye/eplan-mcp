namespace EplanMcp.Packs.Scanning;

/// <summary>
/// A file or folder the engineer gives. <see cref="Kind"/> names the kind of files that are not EPLAN's own (a rule, a
/// mapping, a document); it never turns an EPLAN file into another kind. <see cref="AcceptanceSample"/> marks projects
/// that show what results should look like. <see cref="Official"/>: the engineer states that its rules and mappings are
/// the company's; otherwise they are candidates. Projects (link file and data folder) are referenced, not carried, unless
/// <see cref="BundleProjects"/>.
/// </summary>
public sealed record ScanInput(string Path, ResourceKind? Kind = null, bool AcceptanceSample = false, bool Official = false, bool BundleProjects = false);
