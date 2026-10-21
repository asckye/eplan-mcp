using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// Adds, updates or removes one server in a client's JSON file and keeps the rest as it was: other servers and
/// settings, the order of properties, numbers as written. JSONC comments and trailing commas are accepted, but a rewrite
/// cannot keep the comments; the original stays in the backup (as TIA does). Output is indented by two spaces, with the
/// file's own line ends.
/// </summary>
internal static class JsonClientFile
{
    private static readonly JsonDocumentOptions _read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly JsonSerializerOptions _lf = Indented("\n");

    private static readonly JsonSerializerOptions _crlf = Indented("\r\n");

    /// <summary>The file with <paramref name="server"/> as the entry <see cref="LocalServer.Name"/>.</summary>
    public static ClientFileEdit Write(string text, McpClient client, LocalServer server)
    {
        try
        {
            JsonObject root = Parse(text);
            if (Servers(root, client.ServersKey) is not { } servers)
            {
                servers = [];
                root[client.ServersKey] = servers;
            }

            JsonObject entry = client.JsonEntry(server);
            if (servers.TryGetPropertyValue(LocalServer.Name, out JsonNode? existing) && existing is not JsonObject)
            {
                throw Invalid($"'{client.ServersKey}.{LocalServer.Name}' is not an object");
            }

            if (existing is JsonObject old && old.ContainsKey("command"))
            {
                // Only the launch fields change: env and other options set on the entry stay (TIA ClientProfiles.PrepareSave).
                var updated = (JsonObject)old.DeepClone();
                updated["command"] = entry["command"]!.DeepClone();
                if (entry["args"] is { } arguments)
                {
                    updated["args"] = arguments.DeepClone();
                }

                entry = updated;
            }

            string? before = existing is null ? null : Show(LocalServer.Name, existing);
            if (existing is not null && JsonNode.DeepEquals(existing, entry))
            {
                return new(null, before, before);
            }

            servers[LocalServer.Name] = entry;
            return new(Serialize(root, text), before, Show(LocalServer.Name, entry), HasComments(text));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw Invalid(ex.Message, ex);
        }
    }

    /// <summary>The file without the entry <paramref name="name"/>; unchanged when there is none.</summary>
    public static ClientFileEdit Remove(string text, McpClient client, string name)
    {
        try
        {
            JsonObject root = Parse(text);
            if (Servers(root, client.ServersKey) is not { } servers || !servers.TryGetPropertyValue(name, out JsonNode? existing))
            {
                return new(null, null, null);
            }

            string before = Show(name, existing);
            servers.Remove(name);
            return new(Serialize(root, text), before, null, HasComments(text));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw Invalid(ex.Message, ex);
        }
    }

    /// <summary>The root object; an empty file is an empty object. Duplicate property names throw ArgumentException.</summary>
    private static JsonObject Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        JsonObject root = JsonNode.Parse(text, documentOptions: _read) as JsonObject ?? throw Invalid("the file is not a JSON object");
        _ = root.Count;
        return root;
    }

    private static JsonObject? Servers(JsonObject root, string key)
    {
        if (!root.TryGetPropertyValue(key, out JsonNode? servers))
        {
            return null;
        }

        JsonObject map = servers as JsonObject ?? throw Invalid($"'{key}' is not an object");
        _ = map.Count;
        return map;
    }

    private static string Serialize(JsonObject root, string original)
    {
        bool crlf = original.Contains("\r\n", StringComparison.Ordinal);
        return root.ToJsonString(crlf ? _crlf : _lf) + (crlf ? "\r\n" : "\n");
    }

    /// <summary>One entry as it appears in the file, for the before and after of a preview.</summary>
    private static string Show(string name, JsonNode? entry) =>
        JsonSerializer.Serialize(name, _lf) + ": " + (entry?.ToJsonString(_lf) ?? "null");

    private static bool HasComments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.Comment)
            {
                return true;
            }
        }

        return false;
    }

    private static ClientConfigException Invalid(string detail, Exception? inner = null) => new(ClientProblem.InvalidFile, detail, inner);

    private static JsonSerializerOptions Indented(string newline) => new()
    {
        WriteIndented = true,
        NewLine = newline,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
