using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
namespace EplanMcp.Host.Importing;
/// <summary>Bounded, offline table decoding; row numbers always refer to the source, not the filtered list.</summary>
internal static class PartBuildTable
{
    internal sealed record Row(int Number, string[] Cells);
    internal static List<Row> Read(Stream stream, bool xlsx)
    {
        var rows = new List<Row>();
        if (!xlsx)
        {
            using var text = new StreamReader(stream, new UTF8Encoding(false, true), leaveOpen: true);
            string content = text.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            string header = string.Concat((content.Split('\n').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "").Split('"').Where((_, i) => i % 2 == 0));
            using var csv = new TextFieldParser(new StringReader(content)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            csv.SetDelimiters(header.Count(c => c == ';') > header.Count(c => c == ',') ? ";" : ",");
            while (!csv.EndOfData)
            {
                string[] fields = csv.ReadFields() ?? [];
                int next = csv.LineNumber < 0 ? content.Count(c => c == '\n') + (content.EndsWith('\n') ? 1 : 2) : checked((int)csv.LineNumber);
                Add(new Row(next - 1 - fields.Sum(f => f.Count(c => c == '\n')), fields));
            }
        }
        else
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 512 || zip.Entries.Sum(e => e.Length) > 20 * 1024 * 1024 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            { throw new InvalidDataException("XLSX package exceeds limits or repeats entries."); }
            XDocument Load(string name)
            {
                using Stream entry = (zip.GetEntry(name) ?? throw new InvalidDataException("XLSX entry is missing.")).Open();
                using XmlReader reader = XmlReader.Create(entry, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024 });
                return XDocument.Load(reader);
            }
            foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.Ordinal)))
            { if (Load(entry.FullName).Descendants().Any(e => (string?)e.Attribute("TargetMode") == "External")) { throw new InvalidDataException("External XLSX relationships are forbidden."); } }
            XElement sheet = Load("xl/workbook.xml").Descendants().First(e => e.Name.LocalName == "sheet");
            string id = sheet.Attributes().First(a => a.Name.LocalName == "id").Value;
            string target = Load("xl/_rels/workbook.xml.rels").Descendants().First(e => (string?)e.Attribute("Id") == id).Attribute("Target")?.Value ?? throw new InvalidDataException("Missing worksheet target.");
            var uri = new Uri(new Uri("https://package/xl/"), target);
            if (uri.Host != "package" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            { throw new InvalidDataException("Invalid worksheet target."); }
            string[] shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : [.. Load("xl/sharedStrings.xml").Descendants().Where(e => e.Name.LocalName == "si").Select(e => string.Concat(e.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)))];
            foreach (XElement row in Load(uri.AbsolutePath.TrimStart('/')).Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var cells = new SortedDictionary<int, string>();
                foreach (XElement cell in row.Elements())
                {
                    if (cell.Elements().Any(e => e.Name.LocalName == "f") || (string?)cell.Attribute("t") is "e" or "d" or "b")
                    { throw new InvalidDataException("Formula, date, boolean and error cells are forbidden."); }
                    int column = 0;
                    foreach (char c in ((string?)cell.Attribute("r") ?? "").TakeWhile(char.IsAsciiLetterUpper))
                    { column = checked(column * 26 + c - 'A' + 1); }
                    if (column is < 1 or > 64 || cells.ContainsKey(column) || !int.TryParse(new string(((string?)cell.Attribute("r") ?? "").SkipWhile(char.IsAsciiLetterUpper).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out int cellRow) || cellRow.ToString(CultureInfo.InvariantCulture) != (string?)row.Attribute("r"))
                    { throw new InvalidDataException("Invalid or repeated cell position."); }
                    string value = string.Concat(cell.Descendants().Where(e => e.Name.LocalName is "v" or "t").Select(e => e.Value));
                    cells[column] = (string?)cell.Attribute("t") == "s" ? shared[int.Parse(value, CultureInfo.InvariantCulture)] : value;
                }
                Add(new Row(int.Parse((string?)row.Attribute("r") ?? "0", CultureInfo.InvariantCulture), Enumerable.Range(1, cells.Count == 0 ? 0 : cells.Keys.Max()).Select(i => cells.GetValueOrDefault(i, "")).ToArray()));
            }
        }
        return rows;
        void Add(Row row)
        {
            if (row.Cells.All(string.IsNullOrWhiteSpace))
            { return; }
            if (row.Number is < 1 or > 1048576 || rows.Count >= 51 || row.Cells.Length > 64 || row.Cells.Any(v => v.Length > 500) || rows.Any(r => r.Number >= row.Number))
            { throw new InvalidDataException("Use at most 50 rows, 64 columns and 500 characters per value, in source row order."); }
            rows.Add(row);
        }
    }
}
