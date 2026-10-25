using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Pure validation of snake_case tool JSON before union DTO construction; append every violation.</summary>
public static class PropertyAddressValidation
{
    private static readonly string[] _selectorFields = ["property_id", "property_name", "index"];
    private static readonly string[] _targetFields = ["project_object", "page", "device_tag", "locator"];
    private static readonly string[] _locatorFields = ["project", "kind", "type", "oid", "name", "page", "category", "pos"];
    private static readonly string[] _locatorChecks = ["name", "page", "category", "pos"];

    /// <summary>Resolves only formal names on the exact dictionary type; classification precedes any value access.</summary>
    public static PropertyDictionaryEntry? Selector(JToken? input, string objectType, string path, ICollection<PropertyViolation> violations)
    {
        int before = violations.Count;
        if (!Shape(input, _selectorFields, path, violations, out JObject obj))
        {
            return null;
        }

        bool hasId = obj.ContainsKey("property_id");
        bool hasName = obj.ContainsKey("property_name");
        if (hasId == hasName)
        {
            Add(violations, path, "Specify exactly one property_id or property_name.");
        }

        PropertyDictionaryEntry? entry = null;
        foreach (string field in _selectorFields.Where(obj.ContainsKey))
        {
            JToken token = obj[field]!;
            string at = Child(path, field);
            if (field == "property_name")
            {
                if (!Text(token))
                {
                    Add(violations, at, "Supply an exact formal dictionary name.");
                    continue;
                }

                entry = PropertyCatalog.Find(objectType, token.Value<string>()!);
            }
            else if (Integer(token) is not { } number || number < int.MinValue || number > int.MaxValue || field == "property_id" && number <= 0)
            {
                Add(violations, at, "Supply an integer in range; property IDs must be positive.");
                continue;
            }
            else if (field == "property_id")
            {
                entry = PropertyCatalog.Find(objectType, (int)number);
            }

            if (field != "index" && (Integer(token) == 2000
                || entry?.PropertyId is { } propertyId && PropertyCatalog.ReadRefusal(objectType, propertyId) is not null))
            {
                Add(violations, at, "This property is excluded by the shipped read classification.", ErrorReason.PolicyDenied);
            }
            else if (field != "index" && entry?.PropertyId is null)
            {
                Add(violations, at, "Use a public numeric ID or exact formal name on this dictionary object type.");
            }
        }

        return violations.Count == before ? entry : null;
    }

    /// <summary>Checks four address branches and locator shape; the resolved project path binds locator identity.</summary>
    public static void Target(JToken? input, string resolvedProjectPath, string path, ICollection<PropertyViolation> violations)
    {
        if (!Shape(input, _targetFields, path, violations, out JObject obj))
        {
            return;
        }

        int branches = (obj.ContainsKey("project_object") ? 1 : 0)
            + (obj.ContainsKey("page") || obj.ContainsKey("device_tag") ? 1 : 0) + (obj.ContainsKey("locator") ? 1 : 0);
        if (branches != 1)
        {
            Add(violations, path, "Specify one project, page, device tag with optional page, or locator target.");
        }

        foreach (JProperty field in obj.Properties())
        {
            string at = Child(path, field.Name);
            if (field.Name == "project_object" && !JToken.DeepEquals(field.Value, new JValue(true)))
            {
                Add(violations, at, "project_object must be true.");
            }
            else if (field.Name is "page" or "device_tag" && !Text(field.Value))
            {
                Add(violations, at, "Supply a nonempty exact name.");
            }
            else if (field.Name == "locator")
            {
                Locator(field.Value, resolvedProjectPath, at, violations);
            }
        }
    }

    private static void Locator(JToken input, string project, string path, ICollection<PropertyViolation> violations)
    {
        if (!Shape(input, _locatorFields, path, violations, out JObject obj))
        {
            return;
        }

        if (!Text(obj["project"]) || !ProjectResolver.SamePath(project, obj["project"]!.Value<string>()!))
        {
            Add(violations, Child(path, "project"), "Supply the resolved project's path; cross-project locators are refused.");
        }

        if (obj["kind"]?.Type != JTokenType.String || !LocatorCheck.Kinds.Contains(obj["kind"]!.Value<string>()!, StringComparer.Ordinal))
        {
            Add(violations, Child(path, "kind"), "Locator kind must be page, function or connection.");
        }

        foreach (string field in new[] { "type", "oid" })
        {
            if (Integer(obj[field]) is not > 0 || field == "type" && Integer(obj[field]) > int.MaxValue)
            {
                Add(violations, Child(path, field), "Supply the positive locator integer in range.");
            }
        }

        foreach (string field in _locatorChecks.Where(obj.ContainsKey))
        {
            JToken token = obj[field]!;
            if (token.Type == JTokenType.Null)
            {
                continue;
            }

            if (token.Type != JTokenType.String || field == "pos" && (LocatorCheck.Position(token.Value<string>()) is not { } pos
                || double.IsNaN(pos.X) || double.IsNaN(pos.Y) || double.IsInfinity(pos.X) || double.IsInfinity(pos.Y)))
            {
                Add(violations, Child(path, field), "Supply a check string; position must be finite x,y millimetres.");
            }
        }
    }

    private static bool Shape(JToken? input, string[] fields, string path, ICollection<PropertyViolation> violations, out JObject obj)
    {
        obj = input as JObject ?? new JObject();
        if (input is not JObject)
        {
            Add(violations, path, "Supply an object.");
            return false;
        }

        foreach (JProperty field in obj.Properties().Where(p => !fields.Contains(p.Name, StringComparer.Ordinal)))
        {
            Add(violations, Child(path, field.Name), "Unknown fields are not allowed.");
        }

        return true;
    }

    private static bool Text(JToken? token) => token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(token.Value<string>());

    private static long? Integer(JToken? token) => token?.Type == JTokenType.Integer
        && long.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static string Child(string path, string field) => path + "/" + field.Replace("~", "~0").Replace("/", "~1");

    private static void Add(ICollection<PropertyViolation> violations, string path, string message, string reason = ErrorReason.InvalidArgument) =>
        violations.Add(new PropertyViolation { Path = path, Reason = reason, Message = message });
}
