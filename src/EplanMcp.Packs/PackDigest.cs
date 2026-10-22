using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>
/// The pack digest (M6 design 4.1.2): SHA-256, lowercase hex, of <c>pack.json</c> canonicalised per RFC 8785 (the JSON
/// Canonicalization Scheme): no whitespace, object members sorted by their names' UTF-16 code units, strings escaped as
/// ECMAScript's JSON.stringify does. <c>resources</c> list every file's hash, so the digest covers the whole pack; locks,
/// bindings and plans record it, and the file's layout cannot change it. Manifests hold integers only, so numbers other
/// than integers within ±2^53 are refused rather than canonicalised.
/// </summary>
public static class PackDigest
{
    public static string Compute(byte[] utf8Json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(utf8Json))));

    /// <summary>The RFC 8785 form of <paramref name="utf8Json"/>; throws <see cref="PackFormatException"/>.</summary>
    public static string Canonical(byte[] utf8Json)
    {
        ReadOnlyMemory<byte> json = utf8Json.AsMemory(utf8Json.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64, AllowDuplicateProperties = false });
            var canonical = new StringBuilder();
            Write(document.RootElement, canonical);
            return canonical.ToString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: a string escapes a lone surrogate, which no UTF-16 string can hold.
            throw new PackFormatException($"The file is not valid JSON for a pack: {ex.Message}", ex);
        }
    }

    private static void Write(JsonElement element, StringBuilder output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                foreach ((JsonProperty property, int i) in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select((p, i) => (p, i)))
                {
                    output.Append(i > 0 ? "," : string.Empty);
                    WriteString(property.Name, output);
                    output.Append(':');
                    Write(property.Value, output);
                }

                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                foreach ((JsonElement item, int i) in element.EnumerateArray().Select((e, i) => (e, i)))
                {
                    output.Append(i > 0 ? "," : string.Empty);
                    Write(item, output);
                }

                output.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, output);
                break;
            case JsonValueKind.Number when element.TryGetInt64(out long number) && Math.Abs(number) <= (1L << 53) - 1:
                output.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.Number:
                throw new PackFormatException($"The number {element.GetRawText()} is not an integer within ±2^53; pack files hold no others.");
            default:
                output.Append(element.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", _ => "null" });
                break;
        }
    }

    private static void WriteString(string text, StringBuilder output)
    {
        output.Append('"');
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                output.Append(c).Append(text[++i]);
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw new PackFormatException("A string holds a lone surrogate, which RFC 8785 (I-JSON) does not allow.");
            }

            output.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
                _ => c.ToString(),
            });
        }

        output.Append('"');
    }
}
