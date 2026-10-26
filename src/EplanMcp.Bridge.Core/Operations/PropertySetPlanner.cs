using System.Diagnostics;
using System.Globalization;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>B1 planning rules. Call on the main thread with a complete property-free function inventory (A44).</summary>
public sealed class PropertySetPlanner(IPropertyReadAdapter reader)
{
    /// <summary>Inventory and project languages are copied in the same read work unit; no native resource escapes.</summary>
    public PropertySetPlan Plan(string projectPath, int version, IReadOnlyList<PropertySetChange> changes,
        IReadOnlyList<SnapshotObject> functions, IReadOnlyList<string> languages, CancellationToken cancellationToken)
    {
        var violations = new List<PropertyViolation>();
        Dictionary<string, string>? resolutionMetadata = null;
        var watch = Stopwatch.StartNew();
        var resolved = new List<(SnapshotObject Object, PropertySetChange Change, int Id, int Ordinal)>();
        PropertyRequestValidation.Input(BridgeJson.FromObject(changes).ToString(Formatting.None), violations);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            Add("/project", "An explicit resolved project is required.");
        }

        if (changes.Count is < 1 or > 100)
        {
            Add("/changes", "Supply 1..100 assignments.");
        }

        for (int i = 0; i < changes.Count && i < 100; i++)
        {
            Checkpoint();
            PropertySetChange change = changes[i];
            string at = "/changes/" + i.ToString(CultureInfo.InvariantCulture);
            PropertyTarget? target = change.Target;
            if (target is null || target.ProjectObject == true || target.Locator is { Kind: not "function" }
                || target.DeviceTag is null && target.Locator is null)
            {
                Add(at + "/target", "B1 planning accepts function targets only.", ErrorReason.PolicyDenied);
                continue;
            }

            if (change.Property?.PropertyId is >= 3010 and <= 3013)
            {
                Add(at + "/property", "Personal-information properties are excluded.", ErrorReason.PolicyDenied);
                continue;
            }

            PropertyDictionaryEntry? property = PropertyAddressValidation.Selector(
                Selector(change.Property), "Function", at + "/property", violations);
            PropertyWriteEntry? entry = property?.PropertyId is { } id
                ? PropertyWriteWhitelist.PlanningCandidate(version, "Function", id) : null;
            if (property is not null && entry is null)
            {
                Add(at + "/property", "This target/property is outside the B1 planning candidate scope.", ErrorReason.PolicyDenied);
            }

            if (entry is null)
            {
                continue;
            }

            PropertyValueValidation.Qualifiers(change.Property!.Index is { } index ? new JValue(index) : null,
                change.Language is null ? null : new JValue(change.Language), "", entry, at, violations);
            if (change.Language is not null && !languages.Contains(change.Language, StringComparer.Ordinal))
            {
                Add(at + "/language", "Use an existing project language; planning does not add languages.");
            }

            if (change.Value?.ValueType != entry.Definition.ValueType)
            {
                Add(at + "/value", "Supply the candidate's exact typed value.");
            }
            else
            {
                PropertyValueValidation.Value(new JValue(change.Value!.Text), entry, at + "/value", violations);
            }

            SnapshotObject obj;
            try
            {
                obj = target.Locator is { } locator
                    ? PropertyReadTargets.Located(projectPath, locator, functions, f => f)
                    : PropertyReadTargets.Main(projectPath, target, functions, f => f);
            }
            catch (BridgeErrorException ex)
            {
                resolutionMetadata ??= ex.Error.Metadata;
                Add(at + "/target", ex.Error.Message, ex.Error.Reason);
                continue;
            }

            if (obj.Main != true || string.IsNullOrEmpty(obj.Category))
            {
                Add(at + "/target", "B1 assignments require a main function with a known category.", ErrorReason.PolicyDenied);
            }

            resolved.Add((obj, change, entry.PropertyId, i));
        }

        var keys = new (string Target, int PropertyId, int? Index, string? Language)?[changes.Count];
        foreach (var item in resolved)
        {
            keys[item.Ordinal] = (Key(item.Object), item.Id, item.Change.Property.Index, item.Change.Language);
        }

        PropertyRequestValidation.ResolvedChanges(keys, violations);
        Refuse(violations, resolutionMetadata);
        var result = new PropertySetPlan();
        result.Plan.ProjectPath = projectPath;
        result.Plan.Project = Path.GetFileName(projectPath);
        var names = new HashSet<string?>(resolved.Select(r => r.Object.Name), StringComparer.Ordinal);
        foreach (var item in functions.Where(f => names.Contains(f.Name))
            .GroupBy(Key).Select(g => g.First()).OrderBy(Key, StringComparer.Ordinal)
            .SelectMany(obj => resolved.Where(r => r.Object.Name == obj.Name).Select(r => r.Id).Distinct().OrderBy(id => id)
                .Select(id => (Object: obj, PropertyId: id))))
        {
            SnapshotObject obj = item.Object;
            int propertyId = item.PropertyId;
            Checkpoint();
            var guard = new PropertySetGuard
            {
                Target = new ObjectLocator
                {
                    Project = projectPath,
                    Kind = obj.Kind,
                    Type = obj.Type,
                    Oid = obj.Oid,
                    Name = obj.Name,
                    Page = obj.Page,
                    Category = obj.Category,
                    Pos = obj.Pos,
                },
                Main = obj.Main == true,
                Parts = obj.Parts is null ? null : [.. obj.Parts],
                PropertyId = propertyId,
            };
            using IPropertyReadScope scope = reader.Open(projectPath, new PropertyTarget(locator: guard.Target));
            guard.ObjectType = scope.ObjectType;
            using IPropertyReadHandle? handle = scope.ObjectType == "Function" && PropertyCatalog.ReadRefusal(scope.ObjectType, propertyId) is null
                ? scope.OpenProperty(propertyId) : null;
            using IPropertyReadDefinition? definition = handle?.OpenDefinition();
            PropertyDefinitionMetadata? metadata = definition?.Metadata;
            if (metadata is null || (propertyId == 20011 ? metadata.ValueType != "multi_language"
                : metadata.ValueType != "opaque" || metadata.Format != "ValueWithUnit")
                || metadata.IsIndexed != false || metadata.IsInternal != false
                || metadata.IsReadOnly != false || metadata.IsNamePart != false || metadata.IsComputed == true)
            {
                Add("/changes", "The runtime definition or related-object scope differs from B1.", ErrorReason.PolicyDenied);
                Refuse(violations);
            }

            if (metadata!.IsPropertyLicensed != true)
            {
                Add("/changes", "The candidate property requires a verified runtime licence.", ErrorReason.LicenceMissing);
                Refuse(violations);
            }

            guard.Definition = metadata;
            using IPropertyReadValue? value = handle!.OpenValue(null);
            if (value is null)
            {
                Add("/changes", "A related function has no readable B1 value.", ErrorReason.CapabilityMissing);
                Refuse(violations);
            }

            guard.WasEmpty = value!.IsEmpty;
            if (!guard.WasEmpty)
            {
                if (propertyId == 20027)
                {
                    guard.Before.Add(new SnapshotText { Language = TextLanguages.Independent, Text = value.Display! });
                }
                else
                {
                    using IPropertyReadTexts texts = value.OpenTexts();
                    foreach (string language in texts.Languages.OrderBy(l => l, StringComparer.Ordinal))
                    {
                        Checkpoint();
                        guard.Before.Add(new SnapshotText
                        {
                            Language = language,
                            Text = texts.GetText(language),
                        });
                    }
                }
            }

            guard.After = guard.Before.Select(t => new SnapshotText
            {
                Language = t.Language,
                Text = t.Text,
            }).ToList();
            foreach (var assignment in resolved.Where(r => Key(r.Object) == Key(obj) && r.Id == propertyId))
            {
                if (metadata.MaximumLength is { } maximum && assignment.Change.Value.Text!.Length > maximum)
                {
                    Add("/changes/" + assignment.Ordinal.ToString(CultureInfo.InvariantCulture) + "/value",
                        "Text exceeds the runtime definition's length limit.");
                    Refuse(violations);
                }

                string language = assignment.Change.Language ?? TextLanguages.Independent;
                string before = PropertyValueComparison.TextIn(guard.Before, language);
                string after = assignment.Change.Value.Text!;
                if (string.Equals(before, after, StringComparison.Ordinal))
                {
                    continue;
                }

                guard.After.RemoveAll(t => t.Language == language);
                guard.After.Add(new SnapshotText
                {
                    Language = language,
                    Text = after,
                });
                result.Plan.Changes.Add(new PlannedChange
                {
                    Action = "update",
                    Kind = "function",
                    Name = obj.Name!,
                    Property = propertyId.ToString(CultureInfo.InvariantCulture) + " (" + language + ")",
                    OldValue = before,
                    NewValue = after,
                });
            }

            guard.After = guard.After.OrderBy(t => t.Language, StringComparer.Ordinal).ToList();
            result.Guards.Add(guard);
        }

        Checkpoint();
        result.Plan.Fingerprint = Fingerprint.Of(new[]
        {
            PropertyWriteWhitelist.PlanningVersion, projectPath, version.ToString(CultureInfo.InvariantCulture),
            BridgeJson.FromObject(languages.OrderBy(l => l, StringComparer.Ordinal)).ToString(Formatting.None),
            BridgeJson.FromObject(result.Guards).ToString(Formatting.None),
        });
        if (BridgeJson.ToUtf8(BridgeJson.FromObject(result)).Length > 1024 * 1024)
        {
            Add("/changes", "The complete plan exceeds the Bridge budget. Reduce changes.");
            Refuse(violations);
        }

        Checkpoint();
        return result;

        void Add(string path, string message, string reason = ErrorReason.InvalidArgument)
        {
            violations.Add(new PropertyViolation
            {
                Path = path,
                Message = message,
                Reason = reason,
            });
        }

        void Checkpoint()
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(30))
            {
                throw new BridgeErrorException(ErrorReason.Timeout, "Property planning exceeded 30 seconds. Reduce changes.");
            }
        }
    }

    /// <summary>Execution must freshly re-plan under its own list lifetime before comparing the approved fingerprint.</summary>
    public static void RequireCurrent(PropertySetPlan approved, PropertySetPlan current)
    {
        if (!string.Equals(approved.Plan.Fingerprint, current.Plan.Fingerprint, StringComparison.Ordinal)
            || !JToken.DeepEquals(BridgeJson.FromObject(approved.Guards), BridgeJson.FromObject(current.Guards)))
        {
            Refuse([new PropertyViolation
            {
                Path = "/changes",
                Reason = ErrorReason.PlanStale,
                Message = "Values, dependencies or planning rules changed. Re-plan before executing.",
            }]);
        }
    }

    private static string Key(SnapshotObject obj) => obj.Type.ToString(CultureInfo.InvariantCulture)
        + "/" + obj.Oid.ToString(CultureInfo.InvariantCulture);

    private static JObject? Selector(PropertySelector? selector) => selector is null ? null : new JObject(
        BridgeJson.FromObject(selector).Children<JProperty>().Where(p => p.Value.Type != JTokenType.Null)
            .Select(p => new JProperty(p.Name switch
            {
                "propertyId" => "property_id",
                "propertyName" => "property_name",
                _ => p.Name,
            }, p.Value)));

    private static void Refuse(List<PropertyViolation> violations, Dictionary<string, string>? metadata = null)
    {
        if (violations.Count > 0)
        {
            metadata ??= [];
            metadata["violations"] = BridgeJson.FromObject(violations).ToString(Formatting.None);
            var error = new BridgeErrorException(violations[0].Reason, "Property planning refused. Check all violations and re-plan.",
                metadata: metadata);
            error.Error.Execution = ExecutionState.NotStarted;
            throw error;
        }
    }
}
