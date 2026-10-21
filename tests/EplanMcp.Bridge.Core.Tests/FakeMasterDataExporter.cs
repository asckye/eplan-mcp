using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// The configured parts database as S24 found it on the development machine (api-notes A27), with <see cref="Parts"/>,
/// each part number with its variants. Exports write the file named and record themselves in <see cref="Exports"/>;
/// <see cref="During"/> runs inside each export, as a cancel arriving meanwhile would.
/// </summary>
internal sealed class FakeMasterDataExporter : IMasterDataExporter
{
    public string PartsDatabase { get; set; } = @"$(MD_PARTS)\EPLAN_parts.alk";

    /// <summary>The variants of each part number; the sample database has 464 parts.</summary>
    public Dictionary<string, int> Parts { get; } = new(StringComparer.Ordinal)
    {
        ["SIE.3RH2122-1HB40"] = 1,
        ["PXC.3209510"] = 2,
        ["RIT.8601000"] = 1,
    };

    /// <summary>Thrown by every export, after it wrote its file.</summary>
    public Exception? Failure { get; set; }

    public Action? During { get; set; }

    public List<string> Exports { get; } = [];

    public PartsExport ExportParts(IReadOnlyList<string>? partNumbers, MasterDataFormat format, string? file)
    {
        var found = new PartsExport { Database = PartsDatabase };
        found.Missing.AddRange(partNumbers?.Where(p => !Parts.ContainsKey(p)) ?? []);
        found.PartCount = partNumbers is null ? Parts.Values.Sum() : partNumbers.Where(Parts.ContainsKey).Sum(p => Parts[p]);
        if (file is not null && found.Missing.Count == 0)
        {
            Exports.Add($"parts {format} {(partNumbers is null ? "all" : string.Join(",", partNumbers))} {Path.GetFileName(file)}");
            During?.Invoke();
            File.WriteAllText(file, "<partsmanagement/>");
            ThrowIfFailing();
        }

        return found;
    }

    private void ThrowIfFailing()
    {
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
