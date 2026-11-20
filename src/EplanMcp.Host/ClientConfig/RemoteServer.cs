using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EplanMcp.Host.ClientConfig;

/// <summary>A key-free VM HTTP endpoint; network access is gated by the host-only IPv4 Windows Firewall rule.</summary>
internal sealed partial class RemoteServer(string url)
{
    public const string Name = "eplan-vm";

    private static readonly Regex _secret = new(@"[A-Za-z0-9_-]{43,}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool ValidUrl(string url)
    {
        Match match = Endpoint().Match(url);
        return match.Success && IPAddress.TryParse(match.Groups[1].Value, out IPAddress? ip) && ip.ToString() == match.Groups[1].Value
            && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535;
    }

    public static RemoteServer Create(string url) =>
        ValidUrl(url) ? new(url) : throw new ClientConfigException(ClientProblem.IoError, "Invalid VM URL.");

    public static bool Supports(McpClient client) => client.Name is "claude-code" or "codex" or "gemini-cli" or "qwen-code"
        or "kimi-code" or "codebuddy" or "opencode" or "qwen-agent" or "cursor" or "vscode-copilot";

    public JsonObject JsonEntry(McpClient client)
    {
        var entry = new JsonObject();
        if (client.Name is "claude-code" or "codebuddy" or "vscode-copilot")
        {
            entry["type"] = "http";
        }

        if (client.Name == "opencode")
        {
            entry["type"] = "remote";
        }

        if (client.Name == "qwen-agent")
        {
            entry["type"] = "streamable-http";
        }

        entry[client.Name is "gemini-cli" or "qwen-code" ? "httpUrl" : "url"] = url;
        if (client.Name == "opencode")
        {
            entry["enabled"] = true;
        }

        return entry;
    }

    public string TomlBlock(string newline) => string.Join(newline, $"[mcp_servers.{Name}]", $"url = \"{url}\"",
        "startup_timeout_sec = 120", "tool_timeout_sec = 300", string.Empty);

    // Keep redacting any legacy 43+ char token left in old client files during replacement/removal.
    public static string? Redact(string? text) => text is null ? null : _secret.Replace(text, "<key>");

    [GeneratedRegex(@"\Ahttp://([0-9]{1,3}(?:\.[0-9]{1,3}){3}):([0-9]{1,5})/mcp\z")]
    private static partial Regex Endpoint();
}
