using System.Globalization;
using System.Text;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Pure request budgets and resolved write-key checks; append failures alongside address/value violations.</summary>
public static class PropertyRequestValidation
{
    /// <summary>Measure the complete original tool input, including whitespace, before any reserialization.</summary>
    public static void Input(string originalJson, ICollection<PropertyViolation> violations)
    {
        if (Encoding.UTF8.GetByteCount(originalJson) > 64 * 1024)
        {
            Add(violations, string.Empty, "Tool input exceeds 64 KiB of UTF-8 JSON.");
        }
    }

    /// <summary>Checks original arrays, retaining invalid selectors in the expanded request count.</summary>
    public static void Queries(JToken? queries, ICollection<PropertyViolation> violations)
    {
        if (!Array(queries, 50, "/queries", violations, out JArray items))
        {
            return;
        }

        long requests = 0;
        for (int i = 0; i < items.Count; i++)
        {
            string path = "/queries/" + i.ToString(CultureInfo.InvariantCulture);
            if (items[i] is not JObject query)
            {
                Add(violations, path, "Supply a query object.");
                continue;
            }

            if (Array(query["properties"], 20, path + "/properties", violations, out JArray selectors))
            {
                requests += selectors.Count;
            }
        }

        if (requests > 200)
        {
            Add(violations, "/queries", "Queries exceed 200 property requests.");
        }
    }

    /// <summary>Check the original changes array before address resolution, so invalid changes still count.</summary>
    public static void Changes(JToken? changes, ICollection<PropertyViolation> violations) =>
        Array(changes, 100, "/changes", violations, out _);

    /// <summary>
    /// One slot per original change, null for unresolved addresses. Target is the resolved project-local object identity,
    /// not serialized target JSON; IDs are dictionary-resolved. Aliases must share a target key. No EPLAN access occurs here.
    /// </summary>
    public static void ResolvedChanges(IReadOnlyList<(string Target, int PropertyId, int? Index, string? Language)?> changes,
        ICollection<PropertyViolation> violations)
    {
        var objects = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<(string, int, int?, string?)>();
        for (int i = 0; i < changes.Count; i++)
        {
            if (changes[i] is not { } key)
            {
                continue;
            }

            objects.Add(key.Target);
            if (!keys.Add(key))
            {
                Add(violations, "/changes/" + i.ToString(CultureInfo.InvariantCulture), "Duplicate target, property ID, index and language.");
            }
        }

        if (objects.Count > 50)
        {
            Add(violations, "/changes", "Changes exceed 50 distinct resolved objects.");
        }
    }

    private static bool Array(JToken? input, int maximum, string path, ICollection<PropertyViolation> violations, out JArray items)
    {
        items = input as JArray ?? new JArray();
        if (input is not JArray)
        {
            Add(violations, path, "Supply an array.");
            return false;
        }

        if (items.Count < 1 || items.Count > maximum)
        {
            Add(violations, path, "Array size is outside the published request limits.");
        }

        return true;
    }

    private static void Add(ICollection<PropertyViolation> violations, string path, string message)
    {
        violations.Add(new PropertyViolation
        {
            Path = path,
            Message = message,
        });
    }
}
