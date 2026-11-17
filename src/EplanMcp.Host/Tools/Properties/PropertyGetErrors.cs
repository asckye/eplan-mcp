using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

/// <summary>Preserves Bridge reasons and candidates while rebasing per-query violations to the original tool input.</summary>
internal static class PropertyGetErrors
{
    internal static CallToolResult FromBridge(BridgeError error, int query, IReadOnlyList<int>? selectorOrder = null)
    {
        string root = "/queries/" + query.ToString(CultureInfo.InvariantCulture);
        var metadata = error.Metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(error.Metadata, StringComparer.Ordinal);
        var violations = new List<PropertyViolation>();
        if (metadata.TryGetValue("violations", out string? json))
        {
            try
            {
                violations = JsonSerializer.Deserialize<List<PropertyViolation>>(json, PropertySchemaTools.MetadataJson)?.OfType<PropertyViolation>().ToList() ?? [];
            }
            catch (JsonException)
            {
                // Keep the query pointer when an older Bridge supplies unstructured validation details.
                metadata.Remove("violations");
            }
        }
        foreach (PropertyViolation violation in violations)
        {
            string[] path = violation.Path?.Split('/') ?? [];
            if (selectorOrder is not null && path.Length >= 3 && path[1] == "properties"
                && int.TryParse(path[2], NumberStyles.None, CultureInfo.InvariantCulture, out int selector)
                && selector >= 0 && selector < selectorOrder.Count)
            {
                path[2] = selectorOrder[selector].ToString(CultureInfo.InvariantCulture);
                violation.Path = string.Join('/', path);
            }
            violation.Path = Pointer(violation.Path, root);
        }
        metadata["pointer"] = error.Reason is ErrorReason.AmbiguousName or ErrorReason.DeviceNotFound or ErrorReason.PageNotFound or ErrorReason.PlanStale ? root + "/target" : root;
        if (metadata.ContainsKey("violations"))
        {
            metadata["violations"] = JsonSerializer.Serialize(violations, PropertySchemaTools.MetadataJson);
        }
        return ToolResults.Error(new BridgeError
        {
            Reason = error.Reason,
            Message = error.Message,
            Retryable = error.Retryable,
            Metadata = metadata,
        });
    }

    private static string Pointer(string? path, string root) => path switch
    {
        "/projectPath" => "/project",
        "/language" => "/language",
        "/detailed" => "/response_format",
        _ => root + (path?.StartsWith('/') == true ? string.Join('/', path.Split('/').Select(part =>
            part is "projectObject" or "deviceTag" or "propertyId" or "propertyName" ? JsonNamingPolicy.SnakeCaseLower.ConvertName(part) : part)) : string.Empty),
    };
}
