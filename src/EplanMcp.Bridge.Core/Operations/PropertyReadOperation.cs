using System.Diagnostics;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only P3a core, deliberately unregistered until the P3b adapter passes its Large gates.</summary>
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
            || target["deviceTag"] is { Type: not JTokenType.Null } || target["locator"] is { Type: not JTokenType.Null }
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
            || args.Target.DeviceTag is not null || args.Target.Locator is not null
            || args.Target.ProjectObject != true && string.IsNullOrWhiteSpace(args.Target.Page)
            || args.Properties is null || args.Properties.Count is < 1 or > 20
            || args.Properties.Any(p => p is null || p.PropertyId is null or <= 0 || p.PropertyName is not null)
            || args.SelectorOffset < 0 || args.SelectorOffset >= args.Properties.Count || args.IndexOffset < 0)
        {
            throw InvalidArgument("Supply a resolved project, a project/page target and 1..20 numeric selectors with valid continuation positions.");
        }

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
        using IPropertyReadScope scope = adapter.Open(args.ProjectPath, args.Target);
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
            string? refusal = PropertyCatalog.ReadRefusal(args.Target.ProjectObject == true ? "Project" : "Page", id);
            using IPropertyReadHandle? handle = refusal is null ? scope.OpenProperty(id) : null;
            Checkpoint();
            using IPropertyReadDefinition? definition = handle?.OpenDefinition();
            Checkpoint();
            PropertyDefinitionMetadata? metadata = definition?.Metadata;
            Checkpoint();
            refusal ??= metadata?.IsInternal == true ? "internal" : metadata is { IsIndexed: null } ? "unknown_indexedness" : null;
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
}
