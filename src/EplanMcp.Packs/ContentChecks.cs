using System.Text.Json;
using System.Xml;

namespace EplanMcp.Packs;

/// <summary>
/// Checks of a bundled file's content beyond its hash: an SVG holds no script, event attribute or external reference
/// (ADR-0024 section 1), and an official rule names its source (M6 design 4.2). Each returns the problem or null.
/// </summary>
internal static class ContentChecks
{
    private static readonly XmlReaderSettings _xml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    public static string? Svg(string path)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(path, _xml);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName is "script" or "foreignObject")
                {
                    return $"is an SVG with a <{reader.LocalName}> element, which packs never hold";
                }

                while (reader.MoveToNextAttribute())
                {
                    if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"is an SVG with the event attribute '{reader.Name}', which packs never hold";
                    }

                    if ((reader.LocalName == "href" && !reader.Value.Trim().StartsWith('#')) || HasExternalUrl(reader.Value))
                    {
                        return "is an SVG that refers to something outside itself, which packs never hold";
                    }
                }

                reader.MoveToElement();
                if (reader.LocalName == "style" && reader.ReadElementContentAsString() is var css
                    && (css.Contains("@import", StringComparison.OrdinalIgnoreCase) || HasExternalUrl(css)))
                {
                    return "is an SVG whose style refers to something outside itself, which packs never hold";
                }
            }

            return null;
        }
        catch (XmlException)
        {
            return "is not a well-formed SVG without a DTD";
        }
    }

    /// <summary>An official rule document's <c>spec.source</c>: a file resource and location, or an engineer's confirmation record.</summary>
    public static async Task<string?> RuleSourceAsync(string path, CancellationToken cancellationToken)
    {
        const string Missing = "is an official rule without spec.source (a file resource and location, or the id of an engineer's confirmation); give its source or keep it a candidate";
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Missing;
        }

        try
        {
            using var document = JsonDocument.Parse(await PackVerifier.ReadSmallAsync(path, Path.GetFileName(path), cancellationToken).ConfigureAwait(false));
            return document.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("spec", out JsonElement spec) && spec.ValueKind == JsonValueKind.Object
                && spec.TryGetProperty("source", out JsonElement source)
                && (source.ValueKind == JsonValueKind.Object ? source.EnumerateObject().Any() : source.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(source.GetString()))
                ? null : Missing;
        }
        catch (JsonException)
        {
            return "is an official rule that is not valid JSON";
        }
    }

    /// <summary>A CSS <c>url(…)</c> that is not a fragment (<c>url(#id)</c>) of the same file.</summary>
    private static bool HasExternalUrl(string text)
    {
        for (int at = text.IndexOf("url(", StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf("url(", at + 4, StringComparison.OrdinalIgnoreCase))
        {
            if (!text[(at + 4)..].TrimStart(' ', '\t', '\'', '"').StartsWith('#'))
            {
                return true;
            }
        }

        return false;
    }
}
