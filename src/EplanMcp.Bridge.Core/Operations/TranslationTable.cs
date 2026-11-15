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

    /// <summary>The most keywords one write may ask for; a bigger change belongs in a file import.</summary>
    internal const int MaxKeywords = 200;

    /// <summary>The longest source text or translation; EPLAN's dictionary holds phrases, not documents.</summary>
    internal const int MaxText = 1000;

    /// <summary>The kind every planned keyword change carries, so the approval form groups them.</summary>
    internal const string Kind = "translation_keyword";

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

    /// <summary>
    /// The table a keyword write asks for, checked as arguments rather than as a file: the reference language is the
    /// first column and every language any entry mentions follows, so <see cref="Remap"/> maps it onto the export's
    /// own header. An entry with no translations is still a row, which adds the source text to the dictionary.
    /// </summary>
    public static TranslationTable FromKeywords(IReadOnlyList<TranslationKeywordEntry> keywords, string referenceLanguage)
    {
        var violations = new List<string>();
        if (!Code(referenceLanguage))
        {
            violations.Add("'reference_language' must be a language code such as en_US.");
        }

        if (keywords.Count is 0 or > MaxKeywords)
        {
            violations.Add($"'keywords' must have 1 to {MaxKeywords} entries.");
        }

        var sources = new HashSet<string>(StringComparer.Ordinal);
        var languages = new List<string>();
        for (int i = 0; i < keywords.Count; i++)
        {
            TranslationKeywordEntry entry = keywords[i];
            string at = $"'keywords[{i.ToString(CultureInfo.InvariantCulture)}]";
            if (!Text(entry.Source))
            {
                violations.Add($"{at}.source' must be 1 to {MaxText} characters without a tab, line break or other control character.");
            }
            else if (!sources.Add(entry.Source))
            {
                violations.Add($"{at}.source' repeats a source text named earlier; give each keyword once.");
            }

            foreach (KeyValuePair<string, string> pair in entry.Translations)
            {
                (string language, string text) = (pair.Key, pair.Value);
                if (!Code(language) || language == referenceLanguage)
                {
                    violations.Add($"{at}.translations' must use language codes such as zh_CN, and not 'reference_language'.");
                }
                else if (!Text(text))
                {
                    violations.Add($"{at}.translations.{language}' must be 1 to {MaxText} characters without a tab, line break or other control character.");
                }
                else if (!languages.Contains(language, StringComparer.Ordinal))
                {
                    languages.Add(language);
                }
            }
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.NotStarted(WriteArguments.Invalid(violations));
        }

        languages.Sort(StringComparer.Ordinal);
        string[] columns = [referenceLanguage, .. languages];
        List<string[]> rows = [.. keywords.Select(entry => columns
            .Select(column => column == referenceLanguage ? entry.Source
                : entry.Translations.TryGetValue(column, out string? text) ? text : string.Empty)
            .ToArray())];
        return new TranslationTable(columns, rows, referenceLanguage);
    }

    /// <summary>
    /// What this table would change in the database as it is now: a row for a source text that is not there yet and a
    /// row per translation that is still empty. A language that already reads something is not a change, because a
    /// merging import leaves it alone (api-notes A47); those are returned separately so the caller can say so.
    /// </summary>
    public (List<PlannedChange> Changes, List<string> Kept) Classify(TranslationTable current)
    {
        int[] columns = Positions(current);
        int reference = Array.IndexOf(Columns, ReferenceLanguage);
        Dictionary<string, string[]> rows = current.Index(ReferenceLanguage);
        var changes = new List<PlannedChange>();
        var kept = new List<string>();
        foreach (string[] row in Rows)
        {
            string source = row[reference];
            bool exists = rows.TryGetValue(source, out string[] present);
            if (!exists)
            {
                changes.Add(new PlannedChange { Action = "create", Kind = Kind, Name = source });
            }

            for (int i = 0; i < Columns.Length; i++)
            {
                if (i == reference || row[i].Length == 0)
                {
                    continue;
                }

                string old = exists ? present![columns[i]] : string.Empty;
                if (old.Length == 0)
                {
                    changes.Add(new PlannedChange { Action = "update", Kind = Kind, Name = source, Property = Columns[i], NewValue = row[i] });
                }
                else if (old != row[i])
                {
                    kept.Add(source + " [" + Columns[i] + "] " + old);
                }
            }
        }

        return (changes, kept);
    }

    private static bool Code(string value) => Regex.IsMatch(value, "^[a-z]{2}_[A-Z]{2}$", RegexOptions.CultureInvariant);

    /// <summary>A tab or line break would split the tab file, and the dictionary holds no other control character.</summary>
    private static bool Text(string? value) => value is { Length: > 0 and <= MaxText } && !value.Any(char.IsControl);

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
