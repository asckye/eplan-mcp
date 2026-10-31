using System.Diagnostics;
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
        if (!objects.Any(o => DrawingLocators.Matches(locator, o.Locator)))
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Object identity changed. Call eplan_object_find for a fresh locator."); }
        var target = new PropertyTarget(locator: new ObjectLocator
        { Project = args.Project, Kind = args.ObjectType.ToLowerInvariant(), Type = locator.Type, Oid = locator.Oid });
        var read = new PropertyReadArgs
        {
            ProjectPath = args.Project,
            Target = target,
            Language = args.Language,
            Detailed = args.Detailed,
            SelectorOffset = args.Offset,
            IndexOffset = args.IndexOffset,
            Properties = PropertyCatalog.Entries.Where(e => e.ObjectType == ObjectReadTypes.DictionaryType(args.ObjectType) && e.PropertyId.HasValue)
                .OrderBy(e => e.PropertyId).Select(e => new PropertySelector(propertyId: e.PropertyId)).ToList(),
        };
        if (args.Offset >= read.Properties.Count)
        { throw InvalidArgument("Continuation is outside this object's property set; restart discovery."); }
        result.Properties = PropertyReadOperation.Read(read, () => adapter.Open(args.Project, target), () => watch.Elapsed);
        result.NextOffset = result.Properties.NextSelector;
        return result;
    }
}
