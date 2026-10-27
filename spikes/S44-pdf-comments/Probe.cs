using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S44;

/// <summary>One invocation owns the property lists; EPLAN owns every persistent object (A36/A39/A51).</summary>
internal sealed class Probe(Project project) : IDisposable
{
    private const int SentinelCounter = 944;
    private readonly Dictionary<string, UniversalPropertyList> _lists = new(StringComparer.Ordinal);

    public void Sentinel()
    {
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null);
        if (pages.Any(p => Counter(p) == SentinelCounter))
        {
            throw new InvalidOperationException("Page counter 944 already exists; undo the sentinel or reset the closed fixture.");
        }

        Page template = pages.First(p => p.PageType == DocumentTypeManager.DocumentType.Circuit);
        var source = (PagePropertyList)Properties(template);
        using var name = new PagePropertyList();
        foreach (Eplan.EplApi.DataModel.Properties.Page part in new[]
        {
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLACEOFINSTALLATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_DOCTYPE,
        })
        {
            using PropertyValue value = source[part];
            if (!value.IsEmpty)
            {
                name[part] = value;
            }
        }

        name.PAGE_COUNTER = SentinelCounter.ToString(CultureInfo.InvariantCulture);
#pragma warning disable CA2000 // Persistent page is owned by EPLAN; never Dispose a StorableObject (A39).
        var page = new Page();
#pragma warning restore CA2000
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        using var description = new MultiLangString();
        description.AddString(ISOCode.Language.L_en_US, "S44 sentinel");
        ((PagePropertyList)Properties(page)).PAGE_NOMINATIOMN = description;
    }

    public JObject Read()
    {
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null);
        Comment[] comments = finder.GetAll<Comment>(true).Cast<Comment>().ToArray(); // S34 enumeration; fail rather than silently filter any object.
        var sentinels = new JArray();
        foreach (Page page in pages.Where(p => Counter(p) == SentinelCounter).OrderBy(p => p.ObjectIdentifier))
        {
            var properties = (PagePropertyList)Properties(page);
            sentinels.Add(new JObject
            {
                ["oid"] = page.ObjectIdentifier,
                ["pageName"] = page.Name,
                ["description"] = Value(() => properties.PAGE_NOMINATIOMN, true),
            });
        }

        return new JObject
        {
            ["counts"] = new JObject
            {
                ["pages"] = pages.Length,
                ["functions"] = finder.GetFunctions(null).Length,
                ["connections"] = finder.GetConnections(null).Length,
                ["comments"] = comments.Length,
            },
            ["sentinels"] = sentinels,
            ["comments"] = new JArray(comments.OrderBy(c => c.ObjectIdentifier).Select(ReadComment)),
        };
    }

    private JObject ReadComment(Comment comment)
    {
        var properties = (CommentPropertyList)Properties(comment);
        return new JObject
        {
            ["oid"] = comment.ObjectIdentifier,
            ["identifier"] = comment.ToStringIdentifier(), // Session-scoped; compare oid/page/content after reopening.
            ["runtimeType"] = comment.GetType().FullName,
            ["page"] = Member(() => comment.Page is { } page ? new { oid = page.ObjectIdentifier, name = page.Name } : null),
            ["location"] = Member(() => new { x = comment.Location.X, y = comment.Location.Y }),
            ["author"] = Member(() => comment.Author),
            ["creation"] = Member(() => Date(comment.CreationDate)),
            ["modification"] = Member(() => Date(comment.ModificationDate)),
            ["reviewState"] = Member(() => new { name = comment.ReviewState.ToString(), code = (int)comment.ReviewState }),
            ["answer"] = Member(() => comment.Answer),
            ["text"] = Value(() => properties.TEXT_CONTENTS, true),
            ["properties"] = ReadProperties(properties),
        };
    }

    private static object Date(DateTime value) => new { value = value.ToString("o", CultureInfo.InvariantCulture), kind = value.Kind.ToString() };

    private static JObject Member(Func<object?> read)
    {
        try
        {
            object? value = read();
            return new JObject { ["available"] = true, ["value"] = value is null ? JValue.CreateNull() : JToken.FromObject(value) };
        }
        catch (PropertyNotFoundException)
        {
            return Missing();
        }
    }

    private static JArray ReadProperties(UniversalPropertyList properties)
    {
        AnyPropertyId[] ids = properties.ExistingIds;
        var result = new JArray();
        try
        {
            foreach (AnyPropertyId id in ids.OrderBy(p => p.AsString, StringComparer.Ordinal))
            {
                var entry = new JObject { ["propertyId"] = id.AsString };
                result.Add(entry);
                try
                {
                    using PropertyValue parent = properties[id];
                    using PropertyDefinition definition = parent.Definition;
                    entry["available"] = true;
                    if (definition.IsInternal)
                    {
                        entry["skippedInternal"] = true; // Same exclusion as the product EplanProperties reader.
                        continue;
                    }

                    entry["name"] = definition.Name;
                    entry["type"] = definition.Type.ToString();
                    entry["indexed"] = definition.IsIndexed;
                    bool multilingual = definition.Type == PropertyDefinition.PropertyType.MultilangString;
                    if (definition.IsIndexed)
                    {
                        var values = new JArray();
                        foreach (int index in parent.Indexes.OrderBy(i => i))
                        {
                            JObject value = Value(() => properties[id, index], multilingual);
                            value["index"] = index;
                            values.Add(value);
                        }

                        entry["values"] = values;
                    }
                    else
                    {
                        entry["value"] = Scalar(parent, multilingual);
                    }
                }
                catch (PropertyNotFoundException)
                {
                    entry.Merge(Missing());
                }
            }
        }
        finally
        {
            foreach (AnyPropertyId id in ids)
            {
                id.Dispose();
            }
        }

        return result;
    }

    private static JObject Value(Func<PropertyValue> read, bool multilingual)
    {
        try
        {
            using PropertyValue value = read();
            return Scalar(value, multilingual);
        }
        catch (PropertyNotFoundException)
        {
            return Missing();
        }
    }

    private static JObject Scalar(PropertyValue value, bool multilingual)
    {
        var result = new JObject { ["available"] = true, ["empty"] = value.IsEmpty };
        if (!value.IsEmpty)
        {
            result["raw"] = value.ToString();
            if (multilingual)
            {
                using MultiLangString text = value.ToMultiLangString();
                var languages = new LanguageList();
                int count = text.GetLanguageList(ref languages);
                var texts = new JObject();
                for (int i = 0; i < count; i++)
                {
                    ISOCode.Language language = languages.get_Language(i);
                    using var code = new ISOCode(language);
                    texts[code.GetString()] = text.GetString(language); // Stored text for every present language, no fallback.
                }

                result["texts"] = texts;
            }
        }

        return result;
    }

    private static JObject Missing() => new() { ["available"] = false, ["reason"] = "not available" };

    private int Counter(Page page)
    {
        var properties = (PagePropertyList)Properties(page);
        using PropertyValue value = properties.PAGE_COUNTER;
        return !value.IsEmpty && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int counter) ? counter : -1;
    }

    private UniversalPropertyList Properties(StorableObject target)
    {
        string key = target.ToStringIdentifier();
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = target switch
            {
                Comment comment => comment.Properties,
                Page page => page.Properties,
                _ => throw new InvalidOperationException("Unsupported property owner."),
            };
            _lists.Add(key, list);
        }

        return list;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (UniversalPropertyList list in _lists.Values)
        {
            try
            {
                list.Dispose();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _lists.Clear();
        if (failures.Count > 0)
        {
            throw new AggregateException("Property list release failed.", failures);
        }
    }
}
