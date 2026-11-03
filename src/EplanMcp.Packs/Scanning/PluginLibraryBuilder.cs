using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Scanning;

/// <summary>Builds data-only library sources with the normal pack writer and deterministic archive format.</summary>
public static class PluginLibraryBuilder
{
    public static async Task BuildAsync(string source, string output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        var entries = new List<PluginLibraryEntry>();
        foreach (string folder in Directory.EnumerateDirectories(source).Order(StringComparer.Ordinal))
        {
            PackManifest template = PackJson.ReadManifest(await File.ReadAllBytesAsync(Path.Combine(folder, PackManifest.FileName), cancellationToken).ConfigureAwait(false));
            var resources = new List<ScannedResource>();
            var findings = new List<ScanFinding>();
            foreach (string file in Directory.EnumerateFiles(Path.Combine(folder, "knowledge"), "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                KnowledgeDocument document = KnowledgeDocument.Read(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));
                ScanResult found = await ResourceScanner.ScanAsync([new(file, KnowledgeDocument.ResourceKindOf(document.Kind))], cancellationToken).ConfigureAwait(false);
                resources.AddRange(found.Resources.Select(resource => resource with
                {
                    Resource = resource.Resource with
                    {
                        Path = Path.GetRelativePath(folder, file).Replace('\\', '/'),
                        Source = new() { Input = "knowledge", Path = Path.GetRelativePath(Path.Combine(folder, "knowledge"), file).Replace('\\', '/') },
                    },
                }));
                findings.AddRange(found.Findings);
            }
            var scan = new ScanResult([new() { Name = "knowledge", Kind = PackInputKind.Folder }],
                [.. resources.OrderBy(resource => resource.Resource.Path, StringComparer.Ordinal)], findings);
            PackManifest manifest = ManifestGenerator.Generate(scan,
                new(template.Id, template.Name, template.Version, template.Description, template.Provenance.Author, template.Compatibility.EplanVersions, template.Compatibility.Variants),
                template.Provenance.CreatedBy, template.Provenance.CreatedAt) with
            {
                Kind = template.Kind,
                Provides = template.Provides,
                Standards = template.Standards,
                Configuration = template.Configuration,
            };
            string staging = Path.Combine(output, $".build-{Guid.NewGuid():N}");
            string archive = $"{manifest.Id}-{manifest.Version}{PackArchive.Extension}";
            try
            {
                PackCheckResult check = await PackWriter.WriteAsync(scan, manifest, staging, cancellationToken).ConfigureAwait(false);
                if (!check.Ok)
                { throw new PackFormatException(string.Join("; ", check.Problems)); }
                string exported = Path.Combine(output, $".{Guid.NewGuid():N}{PackArchive.Extension}");
                try
                {
                    await PackArchive.ExportAsync(staging, exported, cancellationToken).ConfigureAwait(false);
                    File.Move(exported, Path.Combine(output, archive), overwrite: true);
                }
                finally { File.Delete(exported); }
                entries.Add(new()
                {
                    Id = manifest.Id,
                    Version = manifest.Version,
                    Kind = manifest.Kind,
                    Digest = PackDigest.Compute(PackJson.Serialize(manifest)),
                    File = archive,
                    Title = manifest.Name,
                    Standards = manifest.Standards ?? [],
                });
            }
            finally
            {
                if (Directory.Exists(staging))
                { Directory.Delete(staging, recursive: true); }
            }
        }
        var index = new PluginLibraryIndex { Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source)), Description = "EplanMcp 内置数据插件", Plugins = entries };
        await File.WriteAllBytesAsync(Path.Combine(output, PluginLibraryReader.FileName), PackJson.Serialize(index), cancellationToken).ConfigureAwait(false);
    }
}
