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

    /// <summary>Prepares the local marketplace; never writes Claude's settings or installs a plugin.</summary>
    public static ClientResult Generate(UserFolders folders, string dataRoot, LocalServer server, DateTimeOffset now, bool dryRun)
    {
        McpClient client = McpClient.Find("claude-code")!;
        string config = client.ConfigFile(folders);
        if (JsonClientFile.Remove(ClientFileChange.Read(config).Text, client, LocalServer.Name).Before is not null)
        {
            throw new ClientConfigException(ClientProblem.ClientConfigured, config);
        }

        string source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(server.Command)!, "claude-plugin"));
        string destination = Path.GetFullPath(Path.Combine(dataRoot, "claude-plugin"));
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ClientConfigException(ClientProblem.IoError, "The data root must be separate from the program directory.");
        }

        string relative = Path.Combine("eplan-mcp", ".mcp.json");
        string template = File.ReadAllText(Path.Combine(source, relative));
        string text = JsonClientFile.Write(template, new McpClient("claude-code", ClientFormat.McpServers), server).Text ?? template;
        ClientFileChange change = ClientFileChange.Read(Path.Combine(destination, relative));
        string? backup = null;
        if (!dryRun)
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (string file in Directory.EnumerateFiles(source, "*", options))
            {
                string name = Path.GetRelativePath(source, file);
                if (name == relative)
                {
                    continue;
                }

                string target = Path.Combine(destination, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            if (change.Text != text)
            {
                backup = change.Apply(text, now);
            }
        }

        return new("claude-code", change.FilePath, ClientAction.Write, dryRun ? ClientOutcome.WouldWrite : ClientOutcome.Written)
        {
            Before = change.Text.Length == 0 ? null : change.Text,
            After = text,
            Backup = backup,
        };
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
