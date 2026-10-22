using System.Security;

namespace EplanMcp.Packs.Scanning;

/// <summary>
/// Turns the files and folders an engineer gives into resources for a pack: classifies each file, hashes it, keeps an
/// EPLAN project's link file and data folder together, and reports what is incomplete or unknown. Reads only, never
/// follows a symbolic link or junction, and names files by input name and relative path, never by local path.
/// </summary>
public static class ResourceScanner
{
    private static readonly string[] _linkExtensions = [".elk", ".ell", ".elr"];

    public static async Task<ScanResult> ScanAsync(IReadOnlyList<ScanInput> inputs, CancellationToken cancellationToken)
    {
        var scan = new Scan();
        foreach (ScanInput input in inputs)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.Path));
            string name = scan.InputName(Path.GetFileName(full));
            FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            if (!info.Exists)
            {
                scan.Add(FindingSeverity.Error, "input_missing", name, "does not exist.");
                continue;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                scan.Add(FindingSeverity.Error, "link_skipped", name, "is a symbolic link or junction; give the files themselves.");
                continue;
            }

            scan.Inputs.Add(new PackInput { Name = name, Kind = info is DirectoryInfo ? PackInputKind.Folder : PackInputKind.File });
            if (info is DirectoryInfo folder && folder.Name.EndsWith(".edb", StringComparison.OrdinalIgnoreCase))
            {
                scan.Add(FindingSeverity.Error, "missing_link_file", name, "is a project's data folder alone; give its link file (.elk) or the folder that holds both.");
            }
            else if (info is DirectoryInfo directory)
            {
                await ScanFolderAsync(scan, directory, input, name, string.Empty, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ScanFileAsync(scan, (FileInfo)info, input, name, string.Empty, cancellationToken).ConfigureAwait(false);
            }
        }

        return new ScanResult(scan.Inputs, [.. scan.Resources.OrderBy(r => r.Resource.Path, StringComparer.Ordinal)], scan.Findings);
    }

    private static async Task ScanFolderAsync(Scan scan, DirectoryInfo directory, ScanInput input, string inputName, string prefix, CancellationToken cancellationToken)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = [.. directory.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            scan.Add(FindingSeverity.Error, "unreadable", Subject(inputName, prefix.TrimEnd('/')), "cannot be listed.");
            return;
        }

        foreach (FileSystemInfo entry in entries)
        {
            string relative = prefix + entry.Name;
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                scan.Add(FindingSeverity.Error, "link_skipped", Subject(inputName, relative), "is a symbolic link or junction; packs never follow links.");
            }
            else if (entry is FileInfo file)
            {
                await ScanFileAsync(scan, file, input, inputName, relative, cancellationToken).ConfigureAwait(false);
            }
            else if (!entry.Name.EndsWith(".edb", StringComparison.OrdinalIgnoreCase))
            {
                await ScanFolderAsync(scan, (DirectoryInfo)entry, input, inputName, relative + "/", cancellationToken).ConfigureAwait(false);
            }
            else if (!_linkExtensions.Any(e => entries.Any(f => f is FileInfo && string.Equals(f.Name, entry.Name[..^4] + e, StringComparison.OrdinalIgnoreCase))))
            {
                scan.Add(FindingSeverity.Error, "missing_link_file", Subject(inputName, relative),
                    $"is a project's data folder without its link file '{entry.Name[..^4]}.elk' beside it; EPLAN needs both.");
            }
        }
    }

    private static async Task ScanFileAsync(Scan scan, FileInfo file, ScanInput input, string inputName, string relative, CancellationToken cancellationToken)
    {
        string subject = Subject(inputName, relative);
        ResourceClassifier.Classification? found = ResourceClassifier.ByExtension(file.Name);
        ResourceClassifier.Classification classification = input.Kind is { } kind && (found is null || !ResourceClassifier.IsEplanFile(found))
            ? new(kind, null, Guessed: false)
            : found ?? new(ResourceKind.Other, null, Guessed: false);
        if (ResourceTypes.Problem(classification.Kind, file.Name) is { } typeProblem)
        {
            scan.Add(FindingSeverity.Error, "type_not_allowed", subject,
                $"{typeProblem}: packs hold EPLAN's own files, JSON, CSV, PNG, SVG, plain text and Markdown (ADR-0024); convert it or leave it out.");
            return;
        }

        try
        {
            IReadOnlyList<(string Relative, string FullPath)> files = classification.Kind == ResourceKind.Project
                ? PackHasher.ProjectFiles(file.FullName)
                : [(file.Name, file.FullName)];
            if (classification.Kind == ResourceKind.Project && files.Count == 1)
            {
                scan.Add(FindingSeverity.Error, "missing_data_folder", subject,
                    $"has no data folder '{Path.ChangeExtension(file.Name, ".edb")}' beside it; EPLAN needs both. Give the folder that holds both.");
                return;
            }

            if (classification.Kind != ResourceKind.Project && file.Length == 0)
            {
                scan.Add(FindingSeverity.Error, "empty_file", subject, "is empty.");
                return;
            }

            (string sha256, long bytes, int count) = classification.Kind == ResourceKind.Project
                ? await PackHasher.HashTreeAsync(files, cancellationToken).ConfigureAwait(false)
                : await HashFileAsync(file.FullName, cancellationToken).ConfigureAwait(false);

            string path = $"resources/{ResourceClassifier.Folder(classification.Kind)}/{(relative.Length == 0 ? file.Name : inputName + "/" + relative)}";
            if (PackPath.Problem(path) is { } problem)
            {
                scan.Add(FindingSeverity.Error, "bad_name", subject, $"cannot be named in a pack: the path {problem}.");
                return;
            }

            if (!scan.Paths.Add(path))
            {
                scan.Add(FindingSeverity.Error, "duplicate_path", subject, $"would land at '{path}' like another input; rename one of them.");
                return;
            }

            var resource = new PackResource
            {
                Path = path,
                Kind = classification.Kind,
                Subtype = classification.Subtype,
                Sha256 = sha256,
                Bytes = bytes,
                Files = count,
                Storage = ResourceStorage.Bundled,
                Source = new ResourceSource { Input = inputName, Path = relative },
            };
            if (classification.Kind == ResourceKind.Project && !input.BundleProjects)
            {
                // Reference projects stay where they are (design 4.1.2, Q6): large, and often with customer data. The
                // input folder stands for the packSources root; TODO(M6): take the root from config.json packSources.
                string rootName = RootName(relative.Length == 0 ? file.Directory?.Name ?? string.Empty : inputName);
                resource = resource with
                {
                    Storage = ResourceStorage.Referenced,
                    Reference = new ResourceReference { Root = rootName, Path = relative.Length == 0 ? file.Name : relative },
                };
                scan.Add(FindingSeverity.Gap, "project_referenced", subject,
                    $"is referenced, not carried: the pack records its hash, facts and EPLAN version, and using it needs config.json packSources to list the root '{rootName}' (not checked yet). To carry a project, give its .zw1 backup.");
            }

            EplanFileInspector.Inspection inspection = await EplanFileInspector.InspectAsync(resource, files, cancellationToken).ConfigureAwait(false);
            scan.Resources.Add(new ScannedResource(resource with { EplanVersion = inspection.EplanVersion }, inspection.Facts, input, files));
            foreach ((FindingSeverity severity, string code, string message) in inspection.Findings)
            {
                scan.Add(severity, code, subject, message);
            }

            if (classification.Guessed)
            {
                scan.Add(FindingSeverity.Warning, "kind_by_extension", subject,
                    $"is taken as {Name(classification.Kind)} from its extension alone; say its kind (such as rule=<path>) if it is something else.");
            }
        }
        catch (PackFormatException ex)
        {
            scan.Add(FindingSeverity.Error, "link_skipped", subject, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            scan.Add(FindingSeverity.Error, "unreadable", subject, "cannot be read; another program may have it open.");
        }
    }

    private static async Task<(string, long, int)> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        (string sha256, long bytes) = await PackHasher.HashFileAsync(path, cancellationToken).ConfigureAwait(false);
        return (sha256, bytes, 1);
    }

    /// <summary>A root name from a folder name: lowercase letters and digits, other characters as '-' (<c>Company name</c> → <c>company-name</c>).</summary>
    private static string RootName(string folder)
    {
        string name = string.Join('-', new string([.. folder.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? "input" : name[..Math.Min(name.Length, PackManifestValidator.MaxIdLength)].TrimEnd('-');
    }

    private static string Subject(string inputName, string relative) => relative.Length == 0 ? inputName : inputName + "/" + relative;

    private static string Name(ResourceKind kind) => kind switch
    {
        ResourceKind.MaterialMapping => "a material mapping",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>What a scan collects; one scan at a time owns it.</summary>
    private sealed class Scan
    {
        public List<PackInput> Inputs { get; } = [];

        public List<ScannedResource> Resources { get; } = [];

        public List<ScanFinding> Findings { get; } = [];

        public HashSet<string> Paths { get; } = new(PackPath.Comparer);

        public void Add(FindingSeverity severity, string code, string subject, string message) => Findings.Add(new(severity, code, subject, $"'{subject}' {message}"));

        /// <summary>The input's last name, made unique among the inputs (<c>Data</c>, <c>Data-2</c>).</summary>
        public string InputName(string name)
        {
            string unique = name.Length == 0 ? "input" : name;
            for (int n = 2; Inputs.Any(i => PackPath.Comparer.Equals(i.Name, unique)); n++)
            {
                unique = $"{name}-{n}";
            }

            return unique;
        }
    }
}
