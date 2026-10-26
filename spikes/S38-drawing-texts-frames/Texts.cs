using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38;

public sealed partial class SpikeS38Action
{
    private static readonly string[] _textKinds = ["freeText", "propertyPlacement", "functionText", "placeholderText", "placeholder", "pathText", "textBase"];
    private static JObject Languages(MultiLangString text)
    {
        var codes = new LanguageList();
        int count = text.GetLanguageList(ref codes);
        var result = new JObject();
        for (int i = 0; i < count; i++)
        {
            ISOCode.Language language = codes.get_Language(i);
            using var code = new ISOCode(language);
            result[code.GetString()] = text.GetString(language); // Exact stored translations, no fallback (A42).
        }
        return result;
    }

    private static JObject Content(TextBase text)
    {
        using MultiLangString content = text switch { PropertyPlacement p => p.Contents, Text t => t.Contents, _ => text.Contents };
        return Languages(content);
    }

    private static List<(Placement Object, string Origin)> Placements(Page page, JObject evidence, bool includeRelated = true)
    {
        var all = new List<(Placement, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var errors = new JArray();
        evidence["traversalErrors"] = errors;
        void Walk(IEnumerable<Placement> items, string origin)
        {
            foreach (Placement item in items)
            {
                try
                {
                    if (!seen.Add(item.ToStringIdentifier())) { continue; }
                    all.Add((item, origin));
                    if (item is Group group) { Walk(group.SubPlacements, origin); }
                    if (includeRelated && item is IPropertyPlacementsContainer container) { Walk(container.PropertyPlacements, origin); }
                    else if (includeRelated && item is SymbolReference symbol) { Walk(symbol.PropertyPlacements, origin); }
                }
                catch (Exception ex) { errors.Add(Evidence.Error(ex)); }
            }
        }
        // Visit the frame first so its contents are not misclassified as editable page free texts.
        try { if (page.PlotFrame is { } frame) { Walk(frame.SubPlacements, "plotFrame"); } }
        catch (Exception ex) { errors.Add(Evidence.Error(ex)); }
        Walk(page.AllPlacements, "page");
        if (includeRelated)
        {
            using var finder = new DMObjectsFinder(page.Project);
            Walk(finder.GetFunctions(null).Where(f => f.Page is { } p && p.ToStringIdentifier() == page.ToStringIdentifier()), "page");
        }
        evidence["placementCount"] = all.Count;
        return all;
    }

    private static JObject Inventory(Page page, PropertyLists lists)
    {
        var result = new JObject();
        var rows = new JArray();
        result["objects"] = rows;
        foreach ((Placement item, string origin) in Placements(page, result))
        {
            if (item is TextBase text)
            {
                JObject row = Identity(item);
                rows.Add(row);
                row["origin"] = origin;
                row["kind"] = item switch { PropertyPlacement => "propertyPlacement", PlaceHolderText => "placeholderText", PathText => "pathText", Text => "freeText", _ => "textBase" };
                Evidence.Capture(row, "layer", () => new JObject { ["id"] = text.LayerId, ["name"] = text.Layer?.Name });
                Evidence.Capture(row, "location", () => new JObject { ["x"] = text.Location.X, ["y"] = text.Location.Y });
                Evidence.Capture(row, "languageMode", () => new JValue(text.LanguageMode.ToString()));
                Evidence.Capture(row, "content", () => Content(text));
                if (item is PropertyPlacement property)
                {
                    row["binding"] = Binding(property, page, origin);
                    Evidence.Capture(row, "displayString", () => new JValue(property.GetDisplayString()));
                }
                if (item is PlaceHolderText placeholder)
                {
                    row["binding"] = Binding(placeholder, page, origin);
                }
            }
            if (item is Function function)
            {
                foreach (int id in new[] { 20011, 20031, 20120 })
                {
                    JObject row = Identity(function);
                    row["kind"] = "functionText";
                    row["origin"] = origin;
                    row["propertyId"] = id;
                    Evidence.Capture(row, "name", () => new JValue(function.Name));
                    Evidence.Capture(row, "layer", () => FunctionTextLayers(function, id));
                    row["layerSource"] = "Associated property placements; Function itself has no graphical layer. Empty means no matching placement was found.";
                    Evidence.Capture(row, "content", () => ReadProperty(lists.Get(function), id, 0));
                    rows.Add(row);
                }
            }
            if (item is IPlaceHolder holder)
            {
                JObject row = Identity(item);
                row["kind"] = "placeholder";
                row["origin"] = origin;
                Evidence.Capture(row, "layer", () => item is GraphicalPlacement g ? new JObject { ["id"] = g.LayerId, ["name"] = g.Layer?.Name } : JValue.CreateNull());
                Evidence.Capture(row, "content", () =>
                {
                    var values = new JArray();
                    foreach (string record in holder.GetRecordNames())
                    {
                        foreach (string variable in holder.GetVariableNames())
                        {
                            var value = new JObject { ["record"] = record, ["variable"] = variable };
                            Evidence.Capture(value, "languages", () => { using MultiLangString text = holder.GetValue(record, variable); return Languages(text); });
                            values.Add(value);
                        }
                    }
                    return new JObject { ["name"] = holder.Name, ["values"] = values };
                });
                rows.Add(row);
            }
        }
        result["counts"] = new JObject(_textKinds
            .Select(k => new JProperty(k, rows.Count(r => r.Value<string>("kind") == k))));
        DiscoveryEvidence.CompleteInventory(result);
        result["coverage"] = "AllPlacements + recursive groups + property containers + finder functions on this page + PlotFrame.SubPlacements; FUNC_TEXT/20011, FUNC_TEXT_AUTOMATIC/20031, FUNC_TEXT_COMMON/20120. Errors are retained; zero is not proof of absence when traversal failed.";
        return result;
    }

    private static JArray FunctionTextLayers(Function function, int number)
    {
        var layers = new JArray();
        foreach (PropertyPlacement placement in function.PropertyPlacements)
        {
            using AnyPropertyId id = placement.DisplayedProperty;
            if (id.AsInt == number) { layers.Add(new JObject { ["placement"] = Identity(placement), ["id"] = placement.LayerId, ["name"] = placement.Layer?.Name }); }
        }
        return layers;
    }

    private static JObject Binding(TextBase placement, Page page, string origin)
    {
        var binding = new JObject();
        // r2 lost the descriptor when Source (or a source identity) threw. Keep each getter independent.
        JToken descriptor = Evidence.Capture(binding, "descriptor", () =>
        {
            using AnyPropertyId id = placement switch { PropertyPlacement p => p.DisplayedProperty, PlaceHolderText p => p.PropDescr, _ => throw new ArgumentException("Not a property text.") };
            int number = id.AsInt;
            return new JObject { ["propertyId"] = number, ["index"] = id.Index,
                ["pageCatalogName"] = Enum.GetName(typeof(Properties.Page), number), ["projectCatalogName"] = Enum.GetName(typeof(Properties.Project), number) };
        });
        if (descriptor["error"] is null)
        {
            foreach (JProperty field in ((JObject)descriptor).Properties()) { binding[field.Name] = field.Value.DeepClone(); }
        }
        JToken source = Evidence.Capture(binding, "source", () =>
        {
            StorableObject? obj = placement switch { PropertyPlacement p => p.Source, PlaceHolderText p => p.SourceObject, _ => null };
            if (obj is null) { return new JObject { ["status"] = "null" }; }
            if (!obj.IsValid) { return new JObject { ["status"] = "invalid", ["type"] = obj.GetType().FullName }; }
            string scope = obj switch
            {
                Page p => p.ToStringIdentifier() == page.ToStringIdentifier() ? "page" : "otherPage",
                Project p => p.ToStringIdentifier() == page.Project.ToStringIdentifier() ? "project" : "otherProject",
                _ => "object",
            };
            return new JObject { ["status"] = "valid", ["scope"] = scope, ["identity"] = Identity(obj) };
        });
        binding["sourceStatus"] = source.Value<string>("status") ?? "unavailable";
        if (placement is PropertyPlacement property)
        {
            Evidence.Capture(binding, "relation", () => new JValue(property.Relation.ToString()));
            Evidence.Capture(binding, "relationIndex", () => new JValue(property.RelationIndex));
        }
        if (placement is PlaceHolderText placeholder)
        {
            Evidence.Capture(binding, "relation", () => new JValue(placeholder.Relation.ToInt()));
            Evidence.Capture(binding, "relationIndex", () => new JValue(placeholder.Relation.Index));
            Evidence.Capture(binding, "groupType", () => new JValue(placeholder.GroupType.ToString()));
            Evidence.Capture(binding, "placeholderType", () => new JValue(placeholder.PlaceHolderType.ToString()));
        }
        DiscoveryEvidence.ResolveScope(binding, origin == "plotFrame" && placement is ContextPropertyPlacement);
        return binding;
    }

    private static JObject ReadProperty(UniversalPropertyList list, int number, int index)
    {
        using AnyPropertyId id = number;
        using PropertyValue value = index > 0 ? list[id, index] : list[id];
        using PropertyDefinition definition = value.Definition;
        var result = new JObject
        {
            ["empty"] = value.IsEmpty, ["type"] = definition.Type.ToString(), ["readOnly"] = definition.IsReadOnly,
            ["namePart"] = definition.IsNamePart, ["internal"] = definition.IsInternal, ["indexed"] = definition.IsIndexed,
        };
        if (!value.IsEmpty)
        {
            if (definition.Type == PropertyDefinition.PropertyType.MultilangString)
            {
                using MultiLangString text = value.ToMultiLangString();
                result["languages"] = Languages(text);
            }
            else { result["display"] = value.ToString(); }
        }
        return result;
    }
}
