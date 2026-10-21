using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host;

internal enum CliCommand
{
    Serve,
    Doctor,
    AuditVerify,
    ClientConfig,
    Version,
}

/// <summary>
/// The command line of <c>eplanmcp</c>. Only doctor, audit verify and client-config take <c>--format json</c>, the
/// output the Workbench reads (M5 design 4.4, 4.11); any other option or format is a wrong command line.
/// <see cref="ClientConfig"/> is set for client-config only; <see cref="Profile"/> matters to serve only.
/// </summary>
internal sealed record CliArguments(CliCommand Command, bool Json, ClientConfigRequest? ClientConfig = null, ToolProfile Profile = ToolProfile.Full)
{
    public static string Usage { get; } =
        "usage: eplanmcp [serve [--profile core|full]] | doctor [--format json] | audit verify [--format json] | client-config <client>... [--dry-run|--remove] [--format json] | --version"
        + Environment.NewLine + "clients: " + string.Join(", ", McpClient.All.Select(c => c.Name));

    /// <summary>The command, or null for a wrong command line.</summary>
    public static CliArguments? Parse(string[] args) => args switch
    {
        [] or ["serve"] => new(CliCommand.Serve, Json: false),
        ["serve", "--profile", "full"] => new(CliCommand.Serve, Json: false),
        ["serve", "--profile", "core"] => new(CliCommand.Serve, Json: false, Profile: ToolProfile.Core),
        ["--version"] => new(CliCommand.Version, Json: false),
        ["doctor", .. var options] => WithFormat(CliCommand.Doctor, options),
        ["audit", "verify", .. var options] => WithFormat(CliCommand.AuditVerify, options),
        ["client-config", .. var options] => ClientConfigArguments(options),
        _ => null,
    };

    private static CliArguments? WithFormat(CliCommand command, string[] options) => options switch
    {
        [] => new(command, Json: false),
        ["--format", "json"] => new(command, Json: true),
        _ => null,
    };

    /// <summary>
    /// Clients and options in any order, each once (a client named twice counts once). Without a client only
    /// <c>--remove</c> is allowed: it removes every recorded entry (M5 design 4.3).
    /// </summary>
    private static CliArguments? ClientConfigArguments(string[] options)
    {
        var clients = new List<McpClient>();
        bool json = false, dryRun = false, remove = false;
        for (int i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--format" when !json && i + 1 < options.Length && options[i + 1] == "json":
                    json = true;
                    i++;
                    break;
                case "--dry-run" when !dryRun:
                    dryRun = true;
                    break;
                case "--remove" when !remove:
                    remove = true;
                    break;
                default:
                    if (McpClient.Find(options[i]) is not { } client)
                    {
                        return null;
                    }

                    if (!clients.Contains(client))
                    {
                        clients.Add(client);
                    }

                    break;
            }
        }

        ClientConfigMode mode = remove ? ClientConfigMode.Remove : dryRun ? ClientConfigMode.DryRun : ClientConfigMode.Write;
        return (dryRun && remove) || (clients.Count == 0 && !remove) ? null : new(CliCommand.ClientConfig, json, new(clients, mode));
    }
}
