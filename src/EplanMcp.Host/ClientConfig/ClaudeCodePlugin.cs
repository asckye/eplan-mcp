using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// Whether the Claude Code plugin <see cref="Id"/> is installed for the user. It already provides the server, and with a
/// second entry for the same command Claude Code starts only one of them (design 4.7, spike S17), so client-config does
/// not write one. Reads <c>~\.claude\settings.json</c> (<c>enabledPlugins</c>) and
/// <c>~\.claude\plugins\installed_plugins.json</c> (<c>plugins</c>), in the formats S17 recorded; never writes them.
/// </summary>
internal static class ClaudeCodePlugin
{
    public const string Id = "eplan-mcp@eplanmcp-local";

    private static readonly JsonDocumentOptions _read = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static bool IsInstalled(UserFolders folders)
    {
        string claude = Path.Combine(folders.UserProfile, ".claude");
        return Lists(Path.Combine(claude, "settings.json"), "enabledPlugins")
            || Lists(Path.Combine(claude, "plugins", "installed_plugins.json"), "plugins");
    }

    private static bool Lists(string file, string key)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(file), documentOptions: _read) is JsonObject root
                && root.TryGetPropertyValue(key, out JsonNode? plugins)
                && plugins is JsonObject map
                && map.ContainsKey(Id);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new ClientConfigException(ClientProblem.InvalidFile, $"{file} cannot be read to check for the plugin {Id}: {ex.Message}", ex);
        }
    }
}
