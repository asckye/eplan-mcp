// Adapted from TIA_Portal_Openness_MCP, src/Studio/Gui/Configuration/ClientProfiles.cs (MergeToml, MigrateToml,
// ScanTomlStrings). Copyright (c) 2026 bulaofen0036-coder. Licensed under the MIT License.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// Codex's config.toml, edited line by line without a TOML library, as TIA does. An existing
/// <c>[mcp_servers.eplan]</c> table keeps its place and its other keys and sub-tables; only <c>command</c> and
/// <c>args</c> are replaced. A new table is appended with TIA's timeouts. Forms this cannot edit safely are refused,
/// not guessed at: <c>mcp_servers</c> as a key, the server defined inside <c>[mcp_servers]</c>, an array of tables, the
/// table twice, and <c>command</c> or <c>args</c> that is set twice or spans lines. Unclosed strings are invalid.
/// </summary>
internal static partial class CodexToml
{
    private const string Servers = "(?:mcp_servers|\"mcp_servers\"|'mcp_servers')";

    private static readonly JsonSerializerOptions _basicString = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ClientFileEdit Write(string text, LocalServer server)
    {
        string newline = Newline(text);
        (string kept, bool tables) = Without(text, LocalServer.Name);
        (string replaced, bool found) = Replace(text, server, newline);
        if (tables && !found)
        {
            // Sub-tables alone imply the table; replacing them with a new one would lose their keys.
            throw Unsupported(null, $"[mcp_servers.{LocalServer.Name}.…] has no [mcp_servers.{LocalServer.Name}] table of its own");
        }

        string result = found ? replaced : (kept.TrimEnd() is { Length: > 0 } rest ? rest + newline + newline : string.Empty) + Block(server, newline);
        string? before = Table(text, LocalServer.Name);
        return result == text ? new(null, before, before) : new(result, before, Table(result, LocalServer.Name));
    }

    public static ClientFileEdit Remove(string text, string name)
    {
        (string kept, bool found) = Without(text, name);
        return found
            ? new(kept.TrimEnd() is { Length: > 0 } rest ? rest + Newline(text) : string.Empty, Table(text, name), null)
            : new(null, null, null);
    }

    public static ClientFileEdit Write(string text, RemoteServer server)
    {
        (string kept, _) = Without(text, RemoteServer.Name);
        string newline = Newline(text);
        string result = (kept.TrimEnd() is { Length: > 0 } rest ? rest + newline + newline : string.Empty) + server.TomlBlock(newline);
        return new(result == text ? null : result, Table(text, RemoteServer.Name), Table(result, RemoteServer.Name));
    }

    /// <summary>The text without the server's table and sub-tables, after refusing the forms it cannot handle (TIA's MergeToml).</summary>
    private static (string Kept, bool Found) Without(string text, string name)
    {
        Regex target = Target(name);
        var inParentKey = new Regex($@"^{Name(name)}\s*(?:=|\.)");
        var array = new Regex($@"^\[\[\s*{Servers}\s*\.\s*{Name(name)}\s*(?:\.|\]\])");
        bool skipping = false, inParent = false, found = false;
        Scan scan = default;
        var kept = new StringBuilder();
        int number = 0;
        foreach (string line in Lines(text))
        {
            number++;
            string trimmed = line.Trim();
            if (scan.AtTopLevel && Header().IsMatch(trimmed))
            {
                skipping = target.IsMatch(trimmed);
                found |= skipping;
                inParent = ServersTable().IsMatch(trimmed);
            }

            if (scan.AtTopLevel && (ServersKey().IsMatch(trimmed) || (inParent && inParentKey.IsMatch(trimmed)) || array.IsMatch(trimmed)))
            {
                throw Unsupported(number, $"'{trimmed}' defines MCP servers outside a [mcp_servers.<name>] table");
            }

            if (!skipping)
            {
                kept.Append(line);
            }

            scan = Next(line, scan, number);
        }

        return scan switch
        {
            { Multiline: not null } => throw new ClientConfigException(ClientProblem.InvalidFile, "A multi-line string is not closed."),
            { Depth: not 0 } => throw new ClientConfigException(ClientProblem.InvalidFile, "An array or inline table is not closed."),
            _ => (kept.ToString(), found),
        };
    }

    /// <summary>
    /// The text with only command and args of the server's own table replaced, and whether there is that table (TIA's
    /// MigrateToml).
    /// </summary>
    private static (string Text, bool Found) Replace(string text, LocalServer server, string newline)
    {
        Regex exact = Exact(LocalServer.Name);
        var result = new StringBuilder();
        bool inside = false, found = false, command = false, args = false;
        Scan scan = default;
        int number = 0;
        foreach (string line in Lines(text))
        {
            number++;
            string trimmed = line.Trim();
            if (scan.AtTopLevel && Header().IsMatch(trimmed))
            {
                Finish();
                inside = exact.IsMatch(trimmed);
                if (inside && found)
                {
                    throw Unsupported(number, $"{trimmed} appears more than once");
                }

                found |= inside;
            }

            Match field = scan.AtTopLevel && inside ? LaunchField().Match(trimmed) : Match.Empty;
            string value = field.Success ? trimmed[field.Length..].Trim() : string.Empty;
            if (!field.Success)
            {
                result.Append(line);
            }
            else if (field.Groups[2].Value == "command")
            {
                if (command || !SingleLineString().IsMatch(value))
                {
                    throw Unsupported(number, "command is set twice or is not a single-line string");
                }

                command = true;
                result.Append(CommandLine(server)).Append(newline);
            }
            else
            {
                if (args || !value.StartsWith('[') || !ArrayEnd().IsMatch(value) || value.Contains("\"\"\"", StringComparison.Ordinal) || value.Contains("'''", StringComparison.Ordinal))
                {
                    throw Unsupported(number, "args is set twice or is not a single-line array");
                }

                args = true;
                result.Append(ArgsLine()).Append(newline);
            }

            scan = Next(line, scan, number);
        }

        Finish();
        return (result.ToString(), found);

        void Finish()
        {
            if (!inside)
            {
                return;
            }

            if (result.Length > 0 && result[^1] != '\n')
            {
                result.Append(newline);
            }

            result.Append(command ? string.Empty : CommandLine(server) + newline).Append(args ? string.Empty : ArgsLine() + newline);
        }
    }

    /// <summary>The server's table and sub-tables as they stand in <paramref name="text"/>, or null.</summary>
    private static string? Table(string text, string name)
    {
        Regex target = Target(name);
        var lines = new List<string>();
        bool inside = false;
        Scan scan = default;
        foreach (string line in Lines(text))
        {
            if (scan.AtTopLevel && Header().IsMatch(line.Trim()))
            {
                inside = target.IsMatch(line.Trim());
            }

            if (inside)
            {
                lines.Add(line.TrimEnd('\r', '\n'));
            }

            scan = Next(line, scan, 0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == 0 ? null : RemoteServer.Redact(string.Join("\n", lines));
    }

    /// <summary>
    /// The scan after <paramref name="line"/>: the multi-line string still open (<c>"""</c> or <c>'''</c>), and how deep in
    /// arrays and inline tables the next line starts. A single-line string left open is invalid; comments end the scan.
    /// </summary>
    private static Scan Next(string line, Scan scan, int number)
    {
        (string? multiline, int depth) = scan;
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            if (multiline is not null)
            {
                if (multiline == "\"\"\"" && line[i] == '\\')
                {
                    i++;
                }
                else if (string.CompareOrdinal(line, i, multiline, 0, 3) == 0)
                {
                    char end = multiline[0];
                    multiline = null;
                    i += 2;
                    while (i + 1 < line.Length && line[i + 1] == end)
                    {
                        i++;
                    }
                }

                continue;
            }

            if (quote != '\0')
            {
                if (quote == '"' && line[i] == '\\')
                {
                    i++;
                }
                else if (line[i] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (line[i] == '#')
            {
                break;
            }

            if (line[i] is '"' or '\'')
            {
                if (i + 2 < line.Length && line[i + 1] == line[i] && line[i + 2] == line[i])
                {
                    multiline = line.Substring(i, 3);
                    i += 2;
                }
                else
                {
                    quote = line[i];
                }
            }
            else
            {
                depth += line[i] switch { '[' or '{' => 1, ']' or '}' => -1, _ => 0 };
            }
        }

        return quote == '\0' ? new(multiline, depth) : throw new ClientConfigException(ClientProblem.InvalidFile, $"Line {number.ToString(CultureInfo.InvariantCulture)}: a string is not closed.");
    }

    /// <summary>
    /// A new table. Codex waits 10 s for a server to start and 60 s for a tool call by default; an approval alone may take
    /// up to 300 s (ADR-0011), so TIA's values are kept.
    /// </summary>
    private static string Block(LocalServer server, string newline) => string.Join(newline,
        $"[mcp_servers.{LocalServer.Name}]", CommandLine(server), ArgsLine(), "startup_timeout_sec = 120", "tool_timeout_sec = 300", string.Empty);

    private static string CommandLine(LocalServer server) => "command = " + BasicString(server.Command);

    private static string ArgsLine() => "args = [" + string.Join(", ", LocalServer.Arguments.Select(BasicString)) + "]";

    /// <summary>A JSON string is a TOML basic string: the same quotes and escapes.</summary>
    private static string BasicString(string value) => JsonSerializer.Serialize(value, _basicString);

    private static string Newline(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string[] Lines(string text) => LineEnd().Split(text);

    private static string Name(string name) => $"(?:{Regex.Escape(name)}|\"{Regex.Escape(name)}\"|'{Regex.Escape(name)}')";

    /// <summary>The server's table or one of its sub-tables.</summary>
    private static Regex Target(string name) => new($@"^\[\s*{Servers}\s*\.\s*{Name(name)}\s*(?:\.[^\]]+)?\s*\]\s*(?:#.*)?$");

    /// <summary>Exactly the server's table.</summary>
    private static Regex Exact(string name) => new($@"^\[\s*{Servers}\s*\.\s*{Name(name)}\s*\]\s*(?:#.*)?$");

    private static ClientConfigException Unsupported(int? number, string detail) =>
        new(ClientProblem.UnsupportedToml, (number is { } line ? $"Line {line.ToString(CultureInfo.InvariantCulture)}: " : string.Empty) + detail + ".");

    [GeneratedRegex("(?<=\n)")]
    private static partial Regex LineEnd();

    /// <summary>
    /// Where a line starts: in a multi-line string or not, and how deep in arrays and inline tables. Only at the top
    /// level can a line be a table header or a key: a line such as <c>["a", "b"]</c> inside a multi-line array is neither.
    /// </summary>
    private readonly record struct Scan(string? Multiline, int Depth)
    {
        public bool AtTopLevel => Multiline is null && Depth == 0;
    }

    [GeneratedRegex(@"^\[.*\]\s*(?:#.*)?$")]
    private static partial Regex Header();

    [GeneratedRegex($@"^\[\s*{Servers}\s*\]\s*(?:#.*)?$")]
    private static partial Regex ServersTable();

    [GeneratedRegex($@"^{Servers}\s*(?:=|\.)")]
    private static partial Regex ServersKey();

    [GeneratedRegex(@"^([""']?)(command|args)\1\s*=")]
    private static partial Regex LaunchField();

    [GeneratedRegex(@"^(""(?:[^""\\]|\\.)*""|'[^']*')\s*(?:#.*)?$")]
    private static partial Regex SingleLineString();

    [GeneratedRegex(@"\]\s*(?:#.*)?$")]
    private static partial Regex ArrayEnd();
}
