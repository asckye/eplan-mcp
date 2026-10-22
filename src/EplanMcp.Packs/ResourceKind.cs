namespace EplanMcp.Packs;

/// <summary>
/// What a resource is: EPLAN's native files (extensions and how far each was verified in <c>docs/eplan/api-notes.md</c>,
/// A35), files the engineer gives, and the knowledge documents EplanMcp generates (M6 design 4.1.3, JSON in the common
/// envelope). The extensions each kind may have are in <see cref="ResourceTypes"/>.
/// </summary>
public enum ResourceKind
{
    /// <summary><c>.zw9</c>.</summary>
    BasicProject,
    /// <summary><c>.zw1</c>.</summary>
    ProjectBackup,
    /// <summary>A link file (<c>.elk</c>, <c>.ell</c>, <c>.elr</c>) with its <c>.edb</c> data folder.</summary>
    Project,
    /// <summary>A window, symbol or page macro.</summary>
    Macro,
    /// <summary>A form (report template).</summary>
    Form,
    /// <summary>A plot frame.</summary>
    Frame,
    /// <summary>A company material mapping (CSV, or a MaterialMapping document).</summary>
    MaterialMapping,
    Document,
    /// <summary>A rule: official or candidate by its slot's standing; an official one names its source.</summary>
    Rule,
    Other,
    /// <summary>A parameterised module.</summary>
    Module,
    ConfigSchema,
    ConfigLayer,
    /// <summary>Missing material or contradicting reference projects.</summary>
    Gap,
    /// <summary>Facts extracted from a reference project's snapshot; only the extractor writes them.</summary>
    FactSet,
    Workflow,
    /// <summary>An acceptance sample: input and the expected check results.</summary>
    Sample,
    /// <summary>A page preview (PNG, or SVG without scripts).</summary>
    Preview,
}
