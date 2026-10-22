using System.Text.RegularExpressions;

namespace EplanMcp.Packs.Scanning;

/// <summary>
/// The kind of a file by its extension; how each EPLAN extension was verified is in docs/eplan/api-notes.md (files of
/// engineering packs). <see cref="Classification.Guessed"/>: the extension alone cannot tell (a CSV may be anything), so
/// the scanner says so.
/// </summary>
internal static partial class ResourceClassifier
{
    private static readonly Dictionary<string, Classification> _byExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zw9"] = new(ResourceKind.BasicProject, null, Guessed: false),
        [".zw1"] = new(ResourceKind.ProjectBackup, null, Guessed: false),
        [".elk"] = new(ResourceKind.Project, null, Guessed: false),
        [".ell"] = new(ResourceKind.Project, "revision_in_progress", Guessed: false),
        [".elr"] = new(ResourceKind.Project, "revision_completed", Guessed: false),
        [".ema"] = new(ResourceKind.Macro, "window_macro", Guessed: false),
        [".ems"] = new(ResourceKind.Macro, "symbol_macro", Guessed: false),
        [".emp"] = new(ResourceKind.Macro, "page_macro", Guessed: false),
        [".fn1"] = new(ResourceKind.Frame, "plot_frame", Guessed: false),
        [".fc2"] = new(ResourceKind.Other, "contour_definition", Guessed: false),
        [".epj"] = new(ResourceKind.Other, "pxf_project", Guessed: false),
        [".zw6"] = new(ResourceKind.Other, "parts_database_backup", Guessed: false),
        [".zw7"] = new(ResourceKind.Other, "translation_database_backup", Guessed: false),
        [".csv"] = new(ResourceKind.MaterialMapping, null, Guessed: true),
        [".xlsx"] = new(ResourceKind.MaterialMapping, null, Guessed: true),
        [".json"] = new(ResourceKind.MaterialMapping, null, Guessed: true),
        [".md"] = new(ResourceKind.Document, null, Guessed: false),
        [".txt"] = new(ResourceKind.Document, null, Guessed: false),
        [".pdf"] = new(ResourceKind.Document, null, Guessed: false),
        [".docx"] = new(ResourceKind.Document, null, Guessed: false),
    };

    /// <summary>The kind of <paramref name="fileName"/>, or null for an extension EplanMcp does not know.</summary>
    public static Classification? ByExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return Form().IsMatch(extension) ? new(ResourceKind.Form, "form", Guessed: false) : _byExtension.GetValueOrDefault(extension);
    }

    /// <summary>EPLAN's own kinds: an engineer's override never turns such a file into another kind.</summary>
    public static bool IsEplanFile(Classification classification) =>
        classification.Kind is not (ResourceKind.MaterialMapping or ResourceKind.Document or ResourceKind.Rule or ResourceKind.Other)
        || classification.Subtype is not null;

    /// <summary>The folder below <c>resources/</c> for a kind.</summary>
    public static string Folder(ResourceKind kind) => kind switch
    {
        ResourceKind.BasicProject => "basic-projects",
        ResourceKind.ProjectBackup => "backups",
        ResourceKind.Project => "projects",
        ResourceKind.Macro => "macros",
        ResourceKind.Form => "forms",
        ResourceKind.Frame => "frames",
        ResourceKind.MaterialMapping => "mappings",
        ResourceKind.Document => "docs",
        ResourceKind.Rule => "rules",
        _ => "other",
    };

    /// <summary>Forms are .f01 to .f99, the number being the report type (observed .f01–.f48 in EPLAN's form folder).</summary>
    [GeneratedRegex(@"^\.[fF]\d\d$", RegexOptions.ECMAScript)]
    private static partial Regex Form();

    internal sealed record Classification(ResourceKind Kind, string? Subtype, bool Guessed);
}
