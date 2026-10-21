using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EplanMcp.Host.ClientConfig;

/// <summary>How a client's file lists its MCP servers, and the shape of a local stdio entry there.</summary>
internal enum ClientFormat
{
    /// <summary>JSON <c>mcpServers</c>; entry <c>{command, args}</c>.</summary>
    McpServers,

    /// <summary>JSON <c>mcpServers</c>; entry <c>{type: "stdio", command, args}</c>.</summary>
    McpServersStdio,

    /// <summary>JSON <c>servers</c>; entry <c>{type: "stdio", command, args}</c> (VS Code).</summary>
    VsCode,

    /// <summary>JSON <c>mcp</c>; entry <c>{type: "local", command: [program, arguments…], enabled: true}</c> (OpenCode).</summary>
    OpenCode,

    /// <summary>TOML table <c>[mcp_servers.&lt;name&gt;]</c> (Codex).</summary>
    CodexToml,
}

/// <summary>
/// An AI client that <c>eplanmcp client-config</c> configures (M5 design 4.11, Q7): its name on the command line, its
/// configuration file and the format of the entry. Files, formats and key names are the ones TIA verified for its local
/// mode (TIA's docs/getting-started/configuration.md, ClientProfiles.RootKey and Entry); Claude Desktop's file is from
/// spike S17 and its format from TIA's McpConfigInstaller.
/// </summary>
internal sealed partial record McpClient(string Name, ClientFormat Format)
{
    /// <summary>The package family of the Claude Desktop MSIX package (spike S17, api-notes A6).</summary>
    public const string ClaudePackageFamily = "Claude_pzs8sxrjxfjjc";

    public static IReadOnlyList<McpClient> All { get; } =
    [
        new("claude-code", ClientFormat.McpServersStdio),
        new("claude-desktop", ClientFormat.McpServers),
        new("codex", ClientFormat.CodexToml),
        new("gemini-cli", ClientFormat.McpServers),
        new("qwen-code", ClientFormat.McpServers),
        new("kimi-code", ClientFormat.McpServers),
        new("codebuddy", ClientFormat.McpServersStdio),
        new("opencode", ClientFormat.OpenCode),
        new("qwen-agent", ClientFormat.McpServers),
        new("cursor", ClientFormat.McpServersStdio),
        new("vscode-copilot", ClientFormat.VsCode),
    ];

    public static McpClient? Find(string name) => All.FirstOrDefault(c => c.Name == name);

    /// <summary>The key of the server map: a JSON property, or the TOML table that holds one table per server.</summary>
    public string ServersKey => Format switch
    {
        ClientFormat.VsCode => "servers",
        ClientFormat.OpenCode => "mcp",
        ClientFormat.CodexToml => "mcp_servers",
        _ => "mcpServers",
    };

    /// <summary>The configuration file on this machine; it need not exist yet.</summary>
    public string ConfigFile(UserFolders folders)
    {
        string home = folders.UserProfile;
        return Name switch
        {
            "claude-code" => Path.Combine(home, ".claude.json"),
            "claude-desktop" => ClaudeDesktopFile(folders),
            "codex" => Path.Combine(folders.ClientHome("CODEX_HOME", Path.Combine(home, ".codex")), "config.toml"),
            "gemini-cli" => Path.Combine(home, ".gemini", "settings.json"),
            "qwen-code" => Path.Combine(home, ".qwen", "settings.json"),
            "kimi-code" => Path.Combine(folders.ClientHome("KIMI_CODE_HOME", Path.Combine(home, ".kimi-code")), "mcp.json"),
            "codebuddy" => Path.Combine(home, ".codebuddy", ".mcp.json"),
            "opencode" => Path.Combine(home, ".config", "opencode", "opencode.json"),
            "qwen-agent" => Path.Combine(home, ".qwen-agent", "mcp.json"),
            "cursor" => Path.Combine(home, ".cursor", "mcp.json"),
            "vscode-copilot" => VsCodeFile(folders),
            _ => throw new UnreachableException($"Client {Name} has no configuration file."),
        };
    }

    /// <summary>The entry that starts <paramref name="server"/>, in this client's JSON format.</summary>
    public JsonObject JsonEntry(LocalServer server)
    {
        JsonNode?[] arguments = [.. LocalServer.Arguments.Select(a => JsonValue.Create(a))];
        return Format switch
        {
            // OpenCode: one array, the program first.
            ClientFormat.OpenCode => new JsonObject { ["type"] = "local", ["command"] = new JsonArray([JsonValue.Create(server.Command), .. arguments]), ["enabled"] = true },
            ClientFormat.McpServers => new JsonObject { ["command"] = server.Command, ["args"] = new JsonArray(arguments) },
            ClientFormat.McpServersStdio or ClientFormat.VsCode => new JsonObject { ["type"] = "stdio", ["command"] = server.Command, ["args"] = new JsonArray(arguments) },
            _ => throw new InvalidOperationException($"{Name} keeps its servers in TOML, not JSON."),
        };
    }

    /// <summary>
    /// Claude Desktop installed as an MSIX package reads the package's copy: processes outside the package never see the
    /// documented %APPDATA%\Claude (spike S17, api-notes A6). Without the package, the documented file.
    /// </summary>
    private static string ClaudeDesktopFile(UserFolders folders)
    {
        string packages = Path.Combine(folders.LocalAppData, "Packages");
        string[] found = Directory.Exists(packages)
            ? [.. Directory.EnumerateDirectories(packages, "Claude_*").Select(Path.GetFileName).OfType<string>().Where(n => PackageFamily().IsMatch(n)).Order(StringComparer.Ordinal)]
            : [];
        string? package = found.FirstOrDefault(n => n.Equals(ClaudePackageFamily, StringComparison.OrdinalIgnoreCase)) ?? (found.Length == 1 ? found[0] : null);
        if (package is null && found.Length > 1)
        {
            throw new ClientConfigException(ClientProblem.AmbiguousPackage, $"Several Claude packages in {packages}: {string.Join(", ", found)}");
        }

        return package is null
            ? Path.Combine(folders.AppData, "Claude", "claude_desktop_config.json")
            : Path.Combine(packages, package, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");
    }

    /// <summary>VS Code's user folder, or the Insiders one when only that one exists (as TIA chooses).</summary>
    private static string VsCodeFile(UserFolders folders)
    {
        string stable = Path.Combine(folders.AppData, "Code", "User");
        string insiders = Path.Combine(folders.AppData, "Code - Insiders", "User");
        return Path.Combine(!Directory.Exists(stable) && Directory.Exists(insiders) ? insiders : stable, "mcp.json");
    }

    /// <summary>A package family name: the package name and the 13-character publisher ID.</summary>
    [GeneratedRegex("^Claude_[a-z0-9]{13}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageFamily();
}
