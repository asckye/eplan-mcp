using System.Globalization;
using System.Xml;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One part number of a parts XML file and how many variants the file has of it.</summary>
public sealed class FilePart
{
    public string PartNumber { get; set; } = string.Empty;

    public int Variants { get; set; }
}

/// <summary>
/// The part numbers of a parts management XML file, as EPLAN exports it (spike S24 r11, r13): the root element
/// <c>partsmanagement</c>, one child <c>part</c> per part with its number in <c>P_ARTICLE_PARTNR</c>, and below it one
/// <c>variant</c> per variant (in S24's export of the sample database 430 parts had 464 variants, as many as the database
/// counts parts). Read with <see cref="XmlReader"/>, no DTD and no resolver, so the file can neither expand entities nor
/// make EPLAN's process fetch anything. Other elements below the root, such as <c>propertydefinition</c>, are skipped.
/// </summary>
public static class PartsXmlFile
{
    public const string Root = "partsmanagement";
    public const string Part = "part";
    public const string Variant = "variant";
    public const string PartNumber = "P_ARTICLE_PARTNR";

    /// <summary>
    /// The file's part numbers in the order they first appear, each once with the variants of all its <c>part</c>
    /// elements (at least one each). INVALID_ARGUMENT, naming <paramref name="file"/> as the tool gave it, when the file
    /// is not such XML, a part has no number, or there is no part.
    /// </summary>
    public static IReadOnlyList<FilePart> Read(string fullPath, string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };
        var parts = new List<FilePart>();
        var byNumber = new Dictionary<string, FilePart>(StringComparer.Ordinal);
        FilePart? current = null;
        int variants = 0;
        try
        {
            using XmlReader reader = XmlReader.Create(fullPath, settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != Root)
            {
                throw Invalid(file, $"its root element is not <{Root}>; export the parts with eplan_part_export (format xml) or EPLAN's parts management");
            }

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth > 2)
                {
                    continue;
                }

                if (reader.Depth == 2)
                {
                    variants += current is not null && reader.LocalName == Variant ? 1 : 0;
                    continue;
                }

                // A new element below the root ends the part before it.
                Finish(current, variants);
                (current, variants) = (null, 0);
                if (reader.LocalName == Part)
                {
                    string number = reader.GetAttribute(PartNumber)?.Trim() ?? string.Empty;
                    current = number.Length > 0 ? Known(number, byNumber, parts) : throw Invalid(file, $"a <{Part}> element has no {PartNumber}");
                }
            }

            Finish(current, variants);
        }
        catch (XmlException ex)
        {
            // The position only: XmlException's message may name the full path (SEC-12).
            throw Invalid(file, string.Format(CultureInfo.InvariantCulture, "it is not well-formed XML (line {0}, position {1})", ex.LineNumber, ex.LinePosition), ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"Invalid arguments: the file '{file}' cannot be read; another program may have it open. Close it there, then call again.",
                ex);
        }

        return parts.Count > 0 ? parts : throw Invalid(file, "it holds no parts");
    }

    private static FilePart Known(string number, Dictionary<string, FilePart> byNumber, List<FilePart> parts)
    {
        if (!byNumber.TryGetValue(number, out FilePart? known))
        {
            known = new FilePart { PartNumber = number };
            byNumber.Add(number, known);
            parts.Add(known);
        }

        return known;
    }

    private static void Finish(FilePart? part, int variants)
    {
        if (part is not null)
        {
            part.Variants += Math.Max(1, variants);
        }
    }

    private static BridgeErrorException Invalid(string file, string problem, Exception? cause = null)
    {
        string violation = $"'file' must be a parts management XML file: '{file}' is not, as {problem}.";
        var error = cause is null
            ? new BridgeErrorException(ErrorReason.InvalidArgument, "Invalid arguments: " + violation)
            : new BridgeErrorException(ErrorReason.InvalidArgument, "Invalid arguments: " + violation, cause);
        error.Error.Metadata = new Dictionary<string, string> { ["violations"] = violation, ["file"] = file };
        return error;
    }
}
