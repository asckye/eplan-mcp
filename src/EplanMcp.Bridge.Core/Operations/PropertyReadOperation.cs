using System.Diagnostics;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only property blocks, mapped and disposed entirely within one main-thread invoke.</summary>
public sealed class PropertyReadOperation(IPropertyReadAdapter adapter, Func<TimeSpan>? elapsed = null,
    Func<DateTimeOffset>? utcNow = null) : Operation<PropertyReadArgs, PropertyReadData>
{
    public override string Name => OperationNames.PropertyRead;

    public override object ParseArgs(JToken? args)
    {
        if (args is not JObject input || input.Properties().Any(p => p.Name is not
            ("projectPath" or "target" or "properties" or "language" or "detailed" or "selectorOffset" or "indexOffset"))
            || input["projectPath"]?.Type != JTokenType.String || input["language"] is { Type: not (JTokenType.String or JTokenType.Null) }
            || input["detailed"] is { Type: not JTokenType.Boolean } || input["selectorOffset"] is { Type: not JTokenType.Integer }
            || input["indexOffset"] is { Type: not JTokenType.Integer } || input["target"] is not JObject target
            || target.Properties().Any(p => p.Name is not ("projectObject" or "page" or "deviceTag" or "locator"))
            || target["projectObject"] is { Type: not (JTokenType.Boolean or JTokenType.Null) }
            || target["page"] is { Type: not (JTokenType.String or JTokenType.Null) }
            || target["deviceTag"] is { Type: not (JTokenType.String or JTokenType.Null) }
            || target["locator"] is { Type: not (JTokenType.Object or JTokenType.Null) } || !ValidTarget(target)
            || input["properties"] is not JArray selectors || selectors.Count is < 1 or > 20
            || selectors.Any(s => s is not JObject item || item.Properties().Any(p => p.Name is not ("propertyId" or "propertyName" or "index"))
                || item["propertyId"]?.Type != JTokenType.Integer || item["propertyName"] is { Type: not JTokenType.Null }
                || item["index"] is { Type: not (JTokenType.Integer or JTokenType.Null) }))
        {
            throw InvalidArgument("Supply an argument object with 1..20 numeric dictionary selectors; unknown fields and coerced indexes are rejected.");
        }

        return base.ParseArgs(args);
    }

    protected override PropertyReadData Execute(PropertyReadArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.ProjectPath) || args.Target is null
            || !ValidTarget((JObject)BridgeJson.FromObject(args.Target))
            || args.Properties is null || args.Properties.Count is < 1 or > 20
            || args.Properties.Any(p => p is null || p.PropertyId is null or <= 0 || p.PropertyName is not null)
            || args.SelectorOffset < 0 || args.SelectorOffset >= args.Properties.Count || args.IndexOffset < 0)
        {
            throw InvalidArgument("Supply a resolved project, one valid property target and 1..20 numeric selectors with valid continuation positions.");
        }

        return Read(args, () => adapter.Open(args.ProjectPath, args.Target), elapsed, utcNow);
    }

    /// <summary>Reads a bounded block, acquiring and disposing one list from the supplied factory (A36/A39).</summary>
    public static PropertyReadData Read(PropertyReadArgs args, Func<IPropertyReadScope> open, Func<TimeSpan>? elapsed = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        var watch = Stopwatch.StartNew();
        Func<TimeSpan> clock = elapsed ?? (() => watch.Elapsed);
        void Checkpoint()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (clock() >= TimeSpan.FromSeconds(20))
            {
                throw new BridgeErrorException(ErrorReason.Timeout, "Property reading exceeded 20 seconds. Reduce the selected batch.");
            }
        }

        Checkpoint();
        using IPropertyReadScope scope = open();
        var result = new PropertyReadData();
        for (int selector = args.SelectorOffset; selector < args.Properties.Count; selector++)
        {
            Checkpoint();
            if (clock() >= TimeSpan.FromSeconds(2))
            {
                return Continue(selector, selector == args.SelectorOffset ? args.IndexOffset : 0);
            }

            PropertySelector property = args.Properties[selector];
            int id = property.PropertyId!.Value;
            int offset = selector == args.SelectorOffset ? args.IndexOffset : 0;
            string? refusal = PropertyCatalog.ReadRefusal(scope.ObjectType, id);
            using IPropertyReadHandle? handle = refusal is null ? scope.OpenProperty(id) : null;
            Checkpoint();
            using IPropertyReadDefinition? definition = handle?.OpenDefinition();
            Checkpoint();
            PropertyDefinitionMetadata? metadata = definition?.Metadata;
            Checkpoint();
            if (handle is not null)
            {
                refusal ??= metadata?.IsInternal == true ? "internal" : metadata?.IsInternal is null ? "unknown_internal"
                    : metadata.IsIndexed is null ? "unknown_indexedness" : null;
            }

            if (refusal is not null || handle is null)
            {
                Add(new PropertyReadRow(refusal is null ? "not_applicable" : "read_refused")
                {
                    Reason = refusal,
                }, property.Index);
            }
            else
            {
                if (property.Index.HasValue && metadata!.IsIndexed != true
                    || offset > 0 && (property.Index.HasValue || metadata!.IsIndexed != true))
                {
                    throw InvalidArgument("An explicit index requires an indexed runtime definition.");
                }

                bool enumerate = metadata!.IsIndexed == true && property.Index is null;
                int?[] indexes = enumerate
                    ? handle.ReadIndexes(offset, 200 - result.Items.Count).Select(i => (int?)i).ToArray() : new[] { property.Index };
                Checkpoint();
                foreach (int? index in indexes)
                {
                    Checkpoint();
                    if (clock() >= TimeSpan.FromSeconds(2))
                    {
                        return Continue(selector, offset);
                    }

                    using IPropertyReadValue? value = handle.OpenValue(index);
                    Checkpoint();
                    Add(PropertyReadMapping.Read(value, metadata, args, args.Language ?? scope.DefaultLanguage, Checkpoint,
                        utcNow ?? (() => DateTimeOffset.UtcNow)), index);
                    offset++;
                    Checkpoint();
                    if (enumerate && (result.Items.Count == 200 || clock() >= TimeSpan.FromSeconds(2)))
                    {
                        return Continue(selector, offset);
                    }
                }

                if (enumerate && indexes.Length == 0 && offset == 0)
                {
                    Add(new PropertyReadRow("empty"), null);
                }
            }

            Checkpoint();
            if (selector + 1 < args.Properties.Count && (result.Items.Count == 200 || clock() >= TimeSpan.FromSeconds(2)))
            {
                return Continue(selector + 1, 0);
            }

            void Add(PropertyReadRow row, int? index)
            {
                row.Definition = args.Detailed && row.Status != "read_refused" ? metadata : null;
                row.Target = args.Target;
                row.PropertyId = id;
                row.Index = index;
                result.Items.Add(row);
            }
        }

        return result;

        PropertyReadData Continue(int selector, int offset)
        {
            result.NextSelector = selector;
            result.NextIndexOffset = offset;
            return result;
        }
    }

    private static bool ValidTarget(JObject target)
    {
        var address = new JObject();
        foreach (JProperty field in target.Properties().Where(p => p.Value.Type != JTokenType.Null))
        {
            string key = field.Name == "projectObject" ? "project_object" : field.Name == "deviceTag" ? "device_tag" : field.Name;
            address[key] = field.Value.DeepClone();
        }

        var violations = new List<PropertyViolation>();
        // Shape validation only; a cross-project identity is refused as PLAN_STALE during resolution.
        JToken? project = (address["locator"] as JObject)?["project"];
        PropertyAddressValidation.Target(address, project?.Type == JTokenType.String ? project.Value<string>()! : string.Empty, "/target", violations);
        return violations.Count == 0;
    }
}
