using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

internal static class PropertyReadMapping
{
    internal static PropertyReadRow Read(IPropertyReadValue? value, PropertyDefinitionMetadata definition,
        PropertyReadArgs args, string defaultLanguage, Action checkpoint, Func<DateTimeOffset> utcNow)
    {
        checkpoint();
        bool empty = value?.IsEmpty ?? false;
        checkpoint();
        if (value is null || empty)
        {
            return new(value is null ? "not_applicable" : "empty");
        }

        bool refused = false;
        int originalLength = 0;
        string? Clean(string? text)
        {
            string? clean = PropertyReadText.CleanText(text, out int length);
            refused |= text is not null && clean is null;
            originalLength = Math.Max(originalLength, length);
            return clean;
        }

        string type = definition.ValueType is "text" or "boolean" or "integer" or "number" or "datetime" or "enum" or "multi_language"
            ? definition.ValueType : "opaque";
        PropertyTypedValue? typed = null;
        string? actualLanguage = null;
        List<SnapshotText>? texts = null;
        if (type == "multi_language")
        {
            using IPropertyReadTexts languages = value.OpenTexts();
            checkpoint();
            IReadOnlyList<string> storedLanguages = languages.Languages;
            checkpoint();
            string selected = args.Language ?? defaultLanguage;
            IEnumerable<string> candidates = new[] { selected, TextLanguages.Independent };
            if (args.Language is null)
            {
                candidates = candidates.Concat(new[] { TextLanguages.English }).Concat(storedLanguages);
            }

            foreach (string code in candidates.Distinct(StringComparer.Ordinal).Where(storedLanguages.Contains))
            {
                checkpoint();
                string text = languages.GetText(code);
                checkpoint();
                if (text.Length > 0)
                {
                    actualLanguage = code;
                    typed = new(type, text: Clean(text) ?? string.Empty);
                    break;
                }
            }

            if (args.Detailed)
            {
                texts = [];
                foreach (string code in storedLanguages.OrderBy(c => c, StringComparer.Ordinal))
                {
                    checkpoint();
                    texts.Add(new SnapshotText
                    {
                        Language = Clean(code) ?? string.Empty,
                        Text = Clean(languages.GetText(code)) ?? string.Empty,
                    });
                    checkpoint();
                }
            }
        }
        else if (type is "text" or "boolean" or "integer" or "number" or "datetime" or "enum")
        {
            object stored = value.ReadStoredValue();
            checkpoint();
            typed = type switch
            {
                "text" => new(type, text: Clean((string)stored) ?? string.Empty),
                "boolean" => new(type, boolean: (bool)stored),
                "integer" => new(type, integerValue: (long)stored),
                "number" => new(type, number: (double)stored),
                "datetime" => new(type, dateTime: (DateTimeOffset)stored),
                _ => stored is string name ? new(type, text: Clean(name) ?? string.Empty) : new(type, integerValue: (long)stored),
            };
        }

        string? display = type == "multi_language" ? args.Detailed ? typed?.Text : null
            : args.Detailed || typed is null ? Clean(value.Display) : null;
        string? unit = args.Detailed ? Clean(definition.Unit) : null;
        string? format = args.Detailed ? Clean(definition.Format) : null;
        checkpoint();
        if (refused)
        {
            return new PropertyReadRow("read_refused")
            {
                Reason = "sensitive_text",
            };
        }

        return new PropertyReadRow(
            type == "multi_language" && typed is null ? "empty" : "value", typed)
        {
            ValueType = type,
            Language = actualLanguage,
            Fallback = type == "multi_language" ? args.Language is null && actualLanguage is not null && actualLanguage != defaultLanguage : null,
            Display = display,
            Texts = texts,
            ReadOnly = args.Detailed ? definition.IsReadOnly : null,
            Computed = args.Detailed ? definition.IsComputed : null,
            ObservedAt = args.Detailed && definition.IsComputed == true ? utcNow() : null,
            Unit = unit,
            Format = format,
            OriginalLength = originalLength > 0 ? originalLength : null,
        };
    }
}
