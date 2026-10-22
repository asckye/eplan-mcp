using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace EplanMcp.Packs.Scanning;

/// <summary>
/// What EplanMcp reads from EPLAN's files without EPLAN (api-notes A35): the 7z signature of backups and basic projects,
/// the database version in a project's link file, the EPLAN version in its <c>ProjectInfo.xml</c> (by property id only:
/// the file also holds the creator's personal data, which is never read), and the root element of EPLAN's XML master
/// data. What needs EPLAN or a 7z reader is reported as a gap.
/// </summary>
internal static partial class EplanFileInspector
{
    private static readonly byte[] _sevenZip = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    /// <summary>The root element's Type in EPLAN's own files of each subtype (A35); symbol macros say WindowMacro too.</summary>
    private static readonly Dictionary<string, string> _pxfTypes = new(StringComparer.Ordinal)
    {
        ["window_macro"] = "WindowMacro",
        ["symbol_macro"] = "WindowMacro",
        ["page_macro"] = "PageMacro",
        ["form"] = "Form",
        ["plot_frame"] = "Frame",
        ["contour_definition"] = "ContourDefinition",
    };

    private static readonly XmlReaderSettings _xml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };

    public static async Task<Inspection> InspectAsync(PackResource resource, IReadOnlyList<(string Relative, string FullPath)> files, CancellationToken cancellationToken)
    {
        var inspection = new Inspection(resource.Path);
        if (resource.Kind is ResourceKind.BasicProject or ResourceKind.ProjectBackup || resource.Subtype is "parts_database_backup" or "translation_database_backup")
        {
            byte[] head = await ReadHeadAsync(files[0].FullPath, 8, cancellationToken).ConfigureAwait(false);
            if (head.AsSpan().StartsWith(_sevenZip) && head.Length == 8)
            {
                inspection.Fact("archive_format", "7z", "file signature 37 7A BC AF 27 1C");
                inspection.Fact("archive_format_version", $"{head[6]}.{head[7]}", "bytes 7 and 8 of the file");
                inspection.Find(FindingSeverity.Gap, "archive_not_inspected",
                    "is a 7z archive; its contents and the EPLAN version it was written with (A_BAKINFO.XML) need a 7z reader or EPLAN, so they were not checked.");
            }
            else
            {
                inspection.Find(FindingSeverity.Error, "not_7z", "is not a 7z archive, though EPLAN writes these files as 7z; it may be damaged or not from EPLAN.");
            }
        }
        else if (resource.Kind == ResourceKind.Project)
        {
            await InspectProjectAsync(inspection, files, cancellationToken).ConfigureAwait(false);
        }
        else if (resource.Subtype is { } subtype && _pxfTypes.TryGetValue(subtype, out string? expected))
        {
            byte[] head = await ReadHeadAsync(files[0].FullPath, 1 << 20, cancellationToken).ConfigureAwait(false);
            (string? type, string? version) = PxfRoot(head);
            if (type is null)
            {
                inspection.Find(FindingSeverity.Warning, "not_eplan_xml", "is not an EPLAN XML file (no EplanPxfRoot root element); EPLAN may not read it.");
            }
            else
            {
                inspection.Fact("pxf_type", type, "EplanPxfRoot Type attribute");
                inspection.Version(version, "EplanPxfRoot Version attribute");
                if (type != expected)
                {
                    inspection.Find(FindingSeverity.Warning, "pxf_type_mismatch", $"says Type '{type}' in its root element; EPLAN's own files with this extension say '{expected}'.");
                }
            }
        }

        return inspection;
    }

    private static async Task InspectProjectAsync(Inspection inspection, IReadOnlyList<(string Relative, string FullPath)> files, CancellationToken cancellationToken)
    {
        (string linkName, string linkPath) = files.First(f => !f.Relative.Contains('/', StringComparison.Ordinal));
        byte[] link = await ReadHeadAsync(linkPath, 4096, cancellationToken).ConfigureAwait(false);
        string text = link.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? Encoding.Unicode.GetString(link, 2, link.Length - 2) : Encoding.UTF8.GetString(link);
        string[] lines = text.TrimStart('﻿').Split('\n', StringSplitOptions.TrimEntries);
        if (lines.Length > 1 && DatabaseVersion().IsMatch(lines[1]))
        {
            inspection.Fact("database_version", lines[1], "line 2 of the link file");
        }

        string info = Path.ChangeExtension(linkName, ".edb") + "/ProjectInfo.xml";
        if (files.FirstOrDefault(f => string.Equals(f.Relative, info, StringComparison.OrdinalIgnoreCase)).FullPath is not { } infoPath)
        {
            inspection.Find(FindingSeverity.Warning, "no_project_info", "has no ProjectInfo.xml in its data folder; its EPLAN version is unknown and the folder may be incomplete.");
        }
        else
        {
            try
            {
                using XmlReader reader = XmlReader.Create(infoPath, _xml);
                while (reader.ReadToFollowing("Property"))
                {
                    // Only these two properties are read; the others include names, addresses and phone numbers.
                    string? id = reader.GetAttribute("id");
                    if (id is "10043" or "10088")
                    {
                        string value = reader.ReadElementContentAsString().Trim();
                        if (id == "10043")
                        {
                            inspection.Version(value, "ProjectInfo.xml property 10043");
                        }
                        else if (EplanVersionRange.TryParseVersion(value, out _))
                        {
                            inspection.Fact("eplan_highest_version", value, "ProjectInfo.xml property 10088");
                        }
                    }
                }
            }
            catch (XmlException)
            {
                inspection.Find(FindingSeverity.Warning, "project_info_unreadable", "has a ProjectInfo.xml that is not well-formed XML; its EPLAN version is unknown.");
            }
        }

        inspection.Find(FindingSeverity.Gap, "eplan_compatibility_unchecked",
            "may or may not open in this EPLAN: ProjectManager.GetProjectDataCompatibility needs EPLAN, so the Bridge has to check it.");
    }

    /// <summary>The Type and Version attributes of an EplanPxfRoot root element, or nulls when the bytes do not start one.</summary>
    private static (string? Type, string? Version) PxfRoot(byte[] head)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(new MemoryStream(head), _xml);
            return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "EplanPxfRoot" && reader.GetAttribute("Type") is { } type
                ? (type, reader.GetAttribute("Version"))
                : (null, null);
        }
        catch (XmlException)
        {
            return (null, null);
        }
    }

    private static async Task<byte[]> ReadHeadAsync(string path, int length, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            byte[] buffer = new byte[(int)Math.Min(length, stream.Length)];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer;
        }
    }

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}$", RegexOptions.ECMAScript)]
    private static partial Regex DatabaseVersion();

    /// <summary>The facts, EPLAN version and findings for one resource.</summary>
    internal sealed class Inspection(string resource)
    {
        public string? EplanVersion { get; private set; }

        public List<PackFact> Facts { get; } = [];

        public List<(FindingSeverity Severity, string Code, string Message)> Findings { get; } = [];

        public void Fact(string name, string value, string evidence) =>
            Facts.Add(new PackFact { Resource = resource, Name = name, Value = value, Evidence = evidence });

        public void Find(FindingSeverity severity, string code, string message) => Findings.Add((severity, code, message));

        /// <summary>Takes <paramref name="value"/> as the resource's EPLAN version when it is one.</summary>
        public void Version(string? value, string evidence)
        {
            if (EplanVersionRange.TryParseVersion(value, out _))
            {
                EplanVersion = value;
                Fact("eplan_version", value!, evidence);
            }
        }
    }
}
