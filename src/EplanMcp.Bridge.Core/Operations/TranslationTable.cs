using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Pure tab-file validation, export-header remapping and read-back comparison (api-notes A47).</summary>
public sealed class TranslationTable
{
    private TranslationTable(string[] columns, List<string[]> rows, string referenceLanguage)
    {
        Columns = columns;
        Rows = rows;
        ReferenceLanguage = referenceLanguage;
    }

    public string[] Columns { get; }

    public IReadOnlyList<string[]> Rows { get; }

    public string ReferenceLanguage { get; }

    /// <summary>Requires a UTF-16 BOM; exports additionally allow COMMENT and empty reference texts.</summary>
    public static TranslationTable Parse(byte[] bytes, string referenceLanguage = "en_US", bool export = false)
    {
        if (bytes.Length < 2 || !((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            throw Invalid([("/file", "Use UTF-16 tab-separated text with a BOM.")]);
        }

        string text;
        try
        {
            text = new UnicodeEncoding(bytes[0] == 0xFE, false, true).GetString(bytes, 2, bytes.Length - 2);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid([("/file", "Correct the malformed UTF-16 text.")]);
        }

        using var reader = new StringReader(text);
        string[] columns = (reader.ReadLine() ?? string.Empty).Split('\t');
        var problems = new List<(string Path, string Message)>();
        if (columns.All(c => c == "COMMENT") || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length || columns.Any(c =>
            !(export && c == "COMMENT") && !Regex.IsMatch(c, "^[a-z]{2}_[A-Z]{2}$", RegexOptions.CultureInvariant)))
        {
            problems.Add(("/file", "Use distinct language codes such as en_US in the first line, separated by tabs."));
        }

        int reference = Array.IndexOf(columns, referenceLanguage);
        if (!export && (reference < 0 || referenceLanguage == "COMMENT"))
        {
            problems.Add(("/reference_language", "Choose one language column from the file header."));
        }

        var rows = new List<string[]>();
        while (reader.ReadLine() is { } line)
        {
            string[] row = line.Split('\t');
            string number = (rows.Count + 2).ToString(CultureInfo.InvariantCulture);
            if (row.Length != columns.Length)
            {
                problems.Add(("/file", $"Line {number} must have the same number of columns as the header."));
            }

            if (!export && reference >= 0 && (reference >= row.Length || string.IsNullOrWhiteSpace(row[reference])))
            {
                problems.Add(("/file", $"Line {number} must have text in the reference-language column."));
            }

            rows.Add(row);
        }

        if (!export && rows.Count == 0)
        {
            problems.Add(("/file", "Include at least one entry below the header."));
        }

        if (problems.Count > 0)
        {
            throw Invalid(problems);
        }

        return new TranslationTable(columns, rows, referenceLanguage);
    }

    /// <summary>Preserves the export's exact header, including COMMENT; the caller writes this as UTF-16.</summary>
    public string Remap(TranslationTable export)
    {
        Positions(export);
        int[] source = export.Columns.Select(c => Array.IndexOf(Columns, c)).ToArray();
        return string.Join("\t", export.Columns) + "\r\n" + string.Join("\r\n", Rows.Select(row =>
            string.Join("\t", source.Select(i => i < 0 ? string.Empty : row[i])))) + "\r\n";
    }

    /// <summary>Counts distinct entries and existing translation cells, never the API's success return (A47).</summary>
    public (int Added, int Filled, int Kept, bool Missing) Compare(TranslationTable before, TranslationTable after)
    {
        int[] oldColumns = Positions(before);
        int[] newColumns = Positions(after);
        int reference = Array.IndexOf(Columns, ReferenceLanguage);
        Dictionary<string, string[]> oldRows = before.Index(ReferenceLanguage);
        Dictionary<string, string[]> newRows = after.Index(ReferenceLanguage);
        var added = new HashSet<string>(StringComparer.Ordinal);
        var filled = new HashSet<(string Text, string Language)>();
        var kept = new HashSet<(string Text, string Language)>();
        bool missing = false;
        foreach (string[] row in Rows)
        {
            string key = row[reference];
            if (!newRows.TryGetValue(key, out string[] current))
            {
                missing = true;
                continue;
            }

            if (!oldRows.TryGetValue(key, out string[] previous))
            {
                added.Add(key);
                continue;
            }

            for (int i = 0; i < Columns.Length; i++)
            {
                if (i == reference || row[i].Length == 0)
                {
                    continue;
                }

                string old = previous[oldColumns[i]];
                string now = current[newColumns[i]];
                if (old.Length == 0 && now == row[i])
                {
                    filled.Add((key, Columns[i]));
                }
                else if (old.Length > 0 && old != row[i] && now == old)
                {
                    kept.Add((key, Columns[i]));
                }
            }
        }

        return (added.Count, filled.Count, kept.Count, missing);
    }

    private int[] Positions(TranslationTable export)
    {
        int[] positions = Columns.Select(c => Array.IndexOf(export.Columns, c)).ToArray();
        if (positions.Any(i => i < 0))
        {
            BridgeErrorException error = Invalid([("/file", "Use only language columns present in the translation database export.")]);
            error.Error.Metadata!["columns"] = JsonConvert.SerializeObject(Columns);
            throw error;
        }

        return positions;
    }

    private Dictionary<string, string[]> Index(string language) => Rows
        .Where(row => row[Array.IndexOf(Columns, language)].Length > 0)
        .GroupBy(row => row[Array.IndexOf(Columns, language)], StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static BridgeErrorException Invalid(IEnumerable<(string Path, string Message)> problems) => WriteArguments.NotStarted(new BridgeErrorException(
        ErrorReason.InvalidArgument, "Correct the translation file or reference_language; see metadata.violations.", false,
        new Dictionary<string, string> { ["violations"] = JsonConvert.SerializeObject(problems.Select(p => new { path = p.Path, message = p.Message })) }));
}
