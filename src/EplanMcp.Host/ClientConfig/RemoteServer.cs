using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.ClientConfig;

/// <summary>A VM endpoint; the secret is only read from stdin and never included in reports or ToString().</summary>
internal sealed partial class RemoteServer(string url, string key)
{
    public const string Name = "eplan-vm";

    private static readonly Regex _secret = new(@"[A-Za-z0-9_-]{43,}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool ValidUrl(string url)
    {
        Match match = Endpoint().Match(url);
        return match.Success && IPAddress.TryParse(match.Groups[1].Value, out IPAddress? ip) && ip.ToString() == match.Groups[1].Value
            && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535;
    }

    public static RemoteServer Read(string url, TextReader input)
    {
        // A key is 43 characters, optionally followed by exactly one LF or CRLF. Bound reads of invalid input too.
        var text = new System.Text.StringBuilder();
        while (text.Length <= 45 && input.Read() is >= 0 and var character)
        {
            text.Append((char)character);
        }

        string key = text.ToString();
        key = key.EndsWith("\r\n", StringComparison.Ordinal) ? key[..^2] : key.EndsWith('\n') ? key[..^1] : key;
        return VmKey.IsValid(key) ? new(url, key) : throw new ClientConfigException(ClientProblem.InvalidVmKey, "Expected one VM key on redirected stdin, optionally followed by one newline.");
    }

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

        entry["headers"] = new JsonObject { ["Authorization"] = "Bearer " + key };
        return entry;
    }

    public string TomlBlock(string newline) => string.Join(newline, $"[mcp_servers.{Name}]", $"url = \"{url}\"",
        $"http_headers = {{ Authorization = \"Bearer {key}\" }}", "startup_timeout_sec = 120", "tool_timeout_sec = 300", string.Empty);

    // Include old keys during replacement/removal and parser diagnostics, not only the supplied key.
    public static string? Redact(string? text) => text is null ? null : _secret.Replace(text, "<key>");

    [GeneratedRegex(@"\Ahttp://([0-9]{1,3}(?:\.[0-9]{1,3}){3}):([0-9]{1,5})/mcp\z")]
    private static partial Regex Endpoint();
}
