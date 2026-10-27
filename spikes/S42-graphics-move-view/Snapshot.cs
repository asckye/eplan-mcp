using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S42;

internal sealed partial class Probe
{
    internal JObject Read()
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var finder = new DMObjectsFinder(project);
        return new JObject
        {
            ["pages"] = new JArray(Pages().Select(page => new JObject
            {
                ["oid"] = page.ObjectIdentifier, ["name"] = page.Name, ["type"] = page.PageType.ToString(),
                ["geometry"] = PageGeometry(page), ["properties"] = PageProperties(page),
                ["placements"] = new JArray(Walk(page.AllPlacements).OrderBy(p => p.ObjectIdentifier).Select(PlacementData)),
            })),
            ["functions"] = new JArray(finder.GetFunctions(null).OrderBy(f => f.ObjectIdentifier).Select(FunctionData)),
            ["partOids"] = new JArray(finder.GetArticleReferences(null).Select(a => a.ObjectIdentifier).OrderBy(x => x)),
            ["connectionOids"] = new JArray(finder.GetConnections(null).Select(c => c.ObjectIdentifier).OrderBy(x => x)),
        };
    }
    private JArray PageProperties(Page page)
    {
        UniversalPropertyList list = Properties(page);
        AnyPropertyId[] ids = list.ExistingIds;
        try
        {
            return new JArray(ids.OrderBy(id => id.AsInt).ThenBy(id => id.Index).Select(id =>
            {
                // r1/r2: ExistingIds reports indexed properties with index 0 (1122 [0]); reading them as unindexed throws lazily.
                try { return Row(id, id.Index > 0 ? list[id, id.Index] : list[id]); }
                catch (IndexedPropertyException)
                {
                    try { return Row(id, list[id, id.Index]); }
                    catch (Exception ex) when (ex is IndexedPropertyException or PropertyNotFoundException) { return new JObject { ["id"] = id.AsInt, ["index"] = id.Index, ["available"] = false, ["error"] = ex.GetType().Name }; }
                }
                catch (PropertyNotFoundException) { return new JObject { ["id"] = id.AsInt, ["index"] = id.Index, ["available"] = false }; }
            }));
        }
        finally { foreach (AnyPropertyId id in ids) { id.Dispose(); } }

        JObject Row(AnyPropertyId id, PropertyValue value)
        {
            using (value)
            {
                using PropertyDefinition definition = value.Definition;
                var row = new JObject { ["id"] = id.AsInt, ["index"] = id.Index, ["empty"] = value.IsEmpty, ["type"] = definition.Type.ToString() };
                if (!value.IsEmpty)
                {
                    if (definition.Type == PropertyDefinition.PropertyType.MultilangString)
                    {
                        using MultiLangString text = value.ToMultiLangString();
                        row["languages"] = Languages(text);
                    }
                    else { row["value"] = value.ToString(); }
                }
                return row;
            }
        }
    }
    private static JObject PageGeometry(Page page) => new()
    {
        ["grid"] = page.GridSize, ["size"] = Optional(() => XY(page.Size)),
        ["bounds"] = Optional(() => new JArray(page.GetBoundingBox().Select(XY))),
        ["plotFrame"] = page.PlotFrame is { } frame ? new JObject { ["name"] = frame.Name, ["size"] = Optional(() => XY(frame.Size)) } : null,
        ["boundaryVerified"] = false, ["coordinateUnit"] = "API units; grid multiples; visible origin/mm relation await real run",
    };
    private static IEnumerable<Placement> Walk(IEnumerable<Placement> roots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Placement>(roots);
        while (pending.Count > 0)
        {
            Placement placement = pending.Pop();
            if (!seen.Add(placement.ToStringIdentifier())) { continue; }
            yield return placement;
            if (placement is Group group) { foreach (Placement child in group.SubPlacements) { pending.Push(child); } }
            IEnumerable<PropertyPlacement> properties = placement is IPropertyPlacementsContainer container ? container.PropertyPlacements : placement is SymbolReference reference ? reference.PropertyPlacements : [];
            foreach (PropertyPlacement child in properties) { pending.Push(child); }
        }
    }
    private static JObject FunctionData(Function function) => new()
    {
        ["oid"] = function.ObjectIdentifier, ["type"] = function.GetType().FullName, ["name"] = function.Name, ["visibleName"] = function.VisibleName,
        ["category"] = function.FunctionCategory.ToString(), ["isPlaced"] = function.IsPlaced,
        ["pageOid"] = function.Page?.ObjectIdentifier, ["location"] = function.IsPlaced ? XY(function.Location) : null,
        ["partOids"] = new JArray(function.ArticleReferences.Select(a => a.ObjectIdentifier).OrderBy(x => x)),
        ["pins"] = new JArray(function.Pins.Select(p => new JObject { ["index"] = p.Index, ["designation"] = p.Designation, ["location"] = XY(p.Location), ["direction"] = p.Direction.ToString() })),
    };
    private static JObject PlacementData(Placement placement)
    {
        var row = new JObject { ["oid"] = placement.ObjectIdentifier, ["type"] = placement.GetType().FullName,
            ["location"] = XY(placement.Location), ["groupOid"] = placement.Group?.ObjectIdentifier,
            ["bounds"] = Optional(() => new JArray(placement.GetBoundingBox().Select(XY))) };
        if (placement is TextBase text)
        {
            using MultiLangString content = text switch { PropertyPlacement p => p.Contents, Text t => t.Contents, _ => text.Contents };
            row["languages"] = Languages(content);
            row["textLocation"] = XY(text.Location);
            row["height"] = text.Height;
            row["justification"] = text.Justification.ToString();
            row["languageMode"] = text.LanguageMode.ToString();
        }
        if (placement is Line line) { row["start"] = XY(line.StartPoint); row["end"] = XY(line.EndPoint); }
        if (placement is Rectangle rectangle) { row["lowerLeft"] = XY(rectangle.LowerLeftCorner); row["upperRight"] = XY(rectangle.UpperRightCorner); }
        if (placement is Group group) { row["memberOids"] = new JArray(group.SubPlacements.Select(p => p.ObjectIdentifier).OrderBy(x => x)); }
        if (placement is Function function)
        {
            row["function"] = FunctionData(function);
            row["library"] = function.SymbolVariant.SymbolLibraryName;
            row["symbol"] = function.SymbolVariant.SymbolName;
            row["variant"] = function.SymbolVariant.VariantNr;
        }
        if (placement is PropertyPlacement property)
        {
            using AnyPropertyId id = property.DisplayedProperty;
            Placement? parent = property.Parent as Placement;
            row["propertyId"] = id.AsInt;
            row["index"] = id.Index;
            row["display"] = property.GetDisplayString();
            row["isVisible"] = property.IsVisible;
            row["parentOid"] = parent?.ObjectIdentifier;
            row["parentLocation"] = parent is null ? null : XY(parent.Location);
            row["locationMinusParent"] = parent is null ? null : XY(new PointD(property.Location.X - parent.Location.X, property.Location.Y - parent.Location.Y));
            row["boundsAbsolute"] = Optional(() => new JArray(property.GetBoundingBox(false).Select(XY)));
            row["boundsRelative"] = Optional(() => new JArray(property.GetBoundingBox(true).Select(XY)));
        }
        return row;
    }
    private static JObject GroupData(Group group) => new() { ["group"] = PlacementData(group), ["members"] = new JArray(Walk(group.SubPlacements).OrderBy(p => p.ObjectIdentifier).Select(PlacementData)) };
    private static string[] Changed(JObject before, JObject after) => before.Properties().Select(p => p.Name).Union(after.Properties().Select(p => p.Name), StringComparer.Ordinal).Where(k => !JToken.DeepEquals(before[k], after[k])).ToArray();
    private static JToken Optional(Func<JToken> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException) { return new JObject { ["unavailable"] = Evidence.Error(ex) }; }
    }
    private static JObject Languages(MultiLangString text)
    {
        var languages = new LanguageList();
        int count = text.GetLanguageList(ref languages);
        var result = new JObject();
        for (int i = 0; i < count; i++)
        {
            ISOCode.Language language = languages.get_Language(i);
            using var code = new ISOCode(language);
            result[code.GetString()] = text.GetString(language);
        }
        return new JObject(result.Properties().OrderBy(p => p.Name, StringComparer.Ordinal));
    }
    private static JObject XY(PointD point) => new() { ["x"] = point.X, ["y"] = point.Y };
    private static bool Same(PointD a, PointD b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
}
