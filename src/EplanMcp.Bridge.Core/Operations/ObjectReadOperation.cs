using System.Diagnostics;
using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public sealed class ObjectReadOperation(IProjectCatalog catalog, IObjectReadAdapter adapter, ObjectReadKind kind) : Operation<ObjectReadArgs, ObjectReadData>
{
    public override string Name => kind == ObjectReadKind.Get ? OperationNames.ObjectGet : OperationNames.ObjectFind;
    public override object ParseArgs(JToken? args)
    {
        if (args is not JObject obj || obj.Properties().Any(p => p.Name is not ("project" or "objectType" or "page" or "nameContains" or "locator" or "language" or "detailed" or "offset" or "indexOffset")
            || (p.Name is "offset" or "indexOffset" ? p.Value.Type != JTokenType.Integer : p.Name == "detailed" ? p.Value.Type != JTokenType.Boolean
                : p.Name == "locator" ? p.Value.Type is not (JTokenType.Object or JTokenType.Null) : p.Value.Type is not (JTokenType.String or JTokenType.Null))))
        { throw InvalidArgument("Use the published read argument types without unknown fields or coercion."); }
        return base.ParseArgs(args);
    }
    /// <summary>
    /// Why the locator found nothing, so the caller does not have to guess: how many objects of this type the
    /// project holds, how many carry its number, and which of the locator's fields the first of those disagrees
    /// with. A fresh locator cannot help when the number is not there at all, and the message alone could not say
    /// so (live 2026-11-14: eplan_object_get refused every locator eplan_object_find had just returned).
    /// </summary>
    private static Dictionary<string, string> Mismatch(DrawingLocator locator, IReadOnlyList<ObjectSummary> objects)
    {
        ObjectSummary[] numbered = [.. objects.Where(o => o.Locator.Type == locator.Type && o.Locator.Oid == locator.Oid).Take(2)];
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["objectsOfThisType"] = objects.Count.ToString(CultureInfo.InvariantCulture),
            ["withThisNumber"] = numbered.Length.ToString(CultureInfo.InvariantCulture),
        };
        if (numbered.FirstOrDefault() is { } other)
        {
            metadata["differs"] = string.Join(",", new[]
            {
                ProjectResolver.SamePath(locator.Project, other.Locator.Project) ? null : "project",
                locator.Page == other.Locator.Page ? null : "page",
                locator.Digest == other.Locator.Digest ? null : "digest",
            }.Where(field => field is not null));
        }
        return metadata;
    }

    protected override ObjectReadData Execute(ObjectReadArgs args)
    {
        if (!ObjectReadTypes.Types.Contains(args.ObjectType)
            || args.Offset < 0 || args.IndexOffset < 0)
        { throw InvalidArgument("Use a shipped object_type and nonnegative continuation positions."); }
        var watch = Stopwatch.StartNew();
        args.Project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project).Path;
        var result = new ObjectReadData();
        if (!ObjectReadTypes.Supported(args.ObjectType))
        {
            result.Unsupported = ObjectReadTypes.UnsupportedReason(args.ObjectType);
            return result;
        }
        IReadOnlyList<ObjectSummary> objects = adapter.Find(args);
        if (kind == ObjectReadKind.Find)
        {
            result.Objects = objects.Skip(args.Offset).Take(200).ToList();
            result.NextOffset = args.Offset + result.Objects.Count < objects.Count ? args.Offset + result.Objects.Count : null;
            return result;
        }
        DrawingLocator locator = args.Locator ?? throw InvalidArgument("Supply the locator returned by eplan_object_find.");
        ObjectSummary? selected = objects.FirstOrDefault(o => DrawingLocators.Matches(locator, o.Locator));
        if (selected is null)
        {
            ObjectSummary[] candidates = objects.Where(o => ProjectResolver.SamePath(locator.Project, o.Locator.Project)
                && (string.IsNullOrEmpty(locator.Page) || locator.Page == o.Locator.Page)
                && locator.Type == o.Locator.Type && locator.Oid == o.Locator.Oid).Take(2).ToArray();
            if (candidates.Length != 1)
            {
                throw new BridgeErrorException(ErrorReason.PlanStale,
                    "Object identity changed. Call eplan_object_find for a fresh locator.", metadata: Mismatch(locator, objects));
            }
            selected = candidates[0];
            result.Hint = "The locator's digest is not this object's identity digest (for example a drawing locator from eplan_page_get); the result's locator is the current object locator.";
        }
        result.Locator = selected.Locator;
        // The object's own kind can be more specific than the requested type, and both the locator check and the
        // property list follow the object, not the request: a Terminal of the PLC category is described as
        // plcterminal (live 2026-11-14: all 31 terminals of the sample redraw are, and object_get refused every
        // one of them with check=not_found while the same locators read under PLCTerminal).
        string objectType = ObjectReadTypes.Types.FirstOrDefault(type =>
            string.Equals(type, selected.Kind, StringComparison.OrdinalIgnoreCase)) ?? args.ObjectType;
        if (!string.Equals(objectType, args.ObjectType, StringComparison.OrdinalIgnoreCase))
        { result.Hint = "This object is a " + objectType + "; its properties are read under that type."; }
        var target = new PropertyTarget(locator: new ObjectLocator
        { Project = args.Project, Kind = objectType.ToLowerInvariant(), Type = selected.Locator.Type, Oid = selected.Locator.Oid });
        var read = new PropertyReadArgs
        {
            ProjectPath = args.Project,
            Target = target,
            Language = args.Language,
            Detailed = args.Detailed,
            SelectorOffset = args.Offset,
            IndexOffset = args.IndexOffset,
            // Object listing keeps the type's own property list (block sizes as before); classification still resolves base lists.
            Properties = PropertyCatalog.Entries.Where(e => e.ObjectType == ObjectReadTypes.DictionaryType(objectType) && e.PropertyId.HasValue)
                .OrderBy(e => e.PropertyId).Select(e => new PropertySelector(propertyId: e.PropertyId)).ToList(),
        };
        if (args.Offset >= read.Properties.Count)
        { throw InvalidArgument("Continuation is outside this object's property set; restart discovery."); }
        result.Properties = PropertyReadOperation.Read(read, () => adapter.Open(args.Project, target), () => watch.Elapsed);
        result.NextOffset = result.Properties.NextSelector;
        return result;
    }
}
