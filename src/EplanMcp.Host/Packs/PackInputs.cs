using EplanMcp.Contracts;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

/// <summary>One input of eplan_pack_import as given: the kind it names, the pack source, the path below the root.</summary>
internal sealed record PackInputArgument(ResourceKind? Kind, string? Source, string Path);

/// <summary>
/// The inputs of eplan_pack_import (docs/tools/eplan_pack_import.md, section 2): <c>[&lt;kind&gt;=][&lt;source&gt;:]&lt;path&gt;</c>,
/// a path below the import root, or below a root that config.json packSources names (ADR-0024 section 6), by the rules
/// of SEC-31 and checked in the Host. <c>&lt;kind&gt;=</c> names the kind of files whose extension does not tell. No
/// message names a root's local path (SEC-12).
/// </summary>
internal static class PackInputs
{
    /// <summary>The kinds an input may name; rules among them are always candidates (M6 design 4.2).</summary>
    public static readonly IReadOnlyDictionary<string, ResourceKind> Kinds = new Dictionary<string, ResourceKind>(StringComparer.Ordinal)
    {
        ["rule"] = ResourceKind.Rule,
        ["material_mapping"] = ResourceKind.MaterialMapping,
        ["document"] = ResourceKind.Document,
    };

    /// <summary>The input <paramref name="given"/> as argument <paramref name="argument"/>, or null after adding its problem to <paramref name="violations"/>.</summary>
    public static PackInputArgument? Parse(string argument, string? given, HostConfig config, List<string> violations)
    {
        string text = given?.Trim() ?? string.Empty;
        ResourceKind? kind = null;
        int equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals > 0)
        {
            if (!Kinds.TryGetValue(text[..equals], out ResourceKind named))
            {
                violations.Add($"'{argument}' starts with '{text[..equals]}=', but only {string.Join(", ", Kinds.Keys.Select(k => k + "="))} name a kind.");
                return null;
            }

            (kind, text) = (named, text[(equals + 1)..]);
        }

        string? source = null;
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 2 && PackManifestValidator.IsId(text[..colon]))
        {
            if (!config.PackSources.ContainsKey(text[..colon]))
            {
                violations.Add($"'{argument}' names the pack source '{text[..colon]}', which config.json packSources does not list; only the engineer can add it there.");
                return null;
            }

            (source, text) = (text[..colon], text[(colon + 1)..]);
        }

        if (text.Length == 0)
        {
            violations.Add($"'{argument}' needs a path below {RootName(source)}, such as refs/Plant or macros/motor.ema.");
            return null;
        }

        if (ImportFiles.PathProblem(argument, text, RootName(source), "refs/Plant") is { } problem)
        {
            violations.Add(problem);
            return null;
        }

        return new PackInputArgument(kind, source, ImportFiles.Normalized(text));
    }

    /// <summary>
    /// What the scanner gets for <paramref name="input"/>: its full path once every step is checked, and for a pack source
    /// the root's name and the path below it. Throws <see cref="ImportFileException"/>: FILE_NOT_FOUND, or INVALID_ARGUMENT
    /// for a link on the way.
    /// </summary>
    public static ScanInput Resolve(string argument, PackInputArgument input, HostDataLocations locations, HostConfig config, bool bundleProjects)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.Source is { } source ? config.PackSources[source] : locations.Imports));
        if (input.Source is null)
        {
            Directory.CreateDirectory(root);
        }

        FileSystemInfo found = ImportFiles.Walk(argument, root, input.Path, RootName(input.Source), folder: true)
            ?? throw new ImportFileException(ErrorReason.FileNotFound, $"Nothing named '{input.Path}' is in {RootName(input.Source)}. Put the files there first, then call again.", argument, input.Path);
        ResourceReference? reference = input.Source is null ? null : new ResourceReference { Root = input.Source, Path = input.Path };
        return new ScanInput(found.FullName, input.Kind, BundleProjects: bundleProjects, Root: reference);
    }

    private static string RootName(string? source) => source is null ? ImportFiles.RootName : $"the pack source '{source}'";
}
