using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host;

internal enum CliCommand
{
    Serve,
    Doctor,
    AuditVerify,
    ClientConfig,
    InstallAddin,
    UninstallAddin,
    Version,
}

/// <summary>
/// The command line of <c>eplanmcp</c>. Only doctor, audit verify, client-config, install-addin and uninstall-addin
/// take <c>--format json</c>, the output the Workbench reads (M5 design 4.4, 4.11); any other option or format is a
/// wrong command line. <see cref="ClientConfig"/> is set for client-config only, <see cref="Variants"/> for install-addin
/// only; <see cref="Profile"/> matters to serve only.
/// </summary>
internal sealed record CliArguments(CliCommand Command, bool Json, ClientConfigRequest? ClientConfig = null, ToolProfile Profile = ToolProfile.Full, IReadOnlyList<string>? Variants = null)
{
    public static string Usage { get; } =
        "usage: eplanmcp [serve [--profile core|full]] | doctor [--format json] | audit verify [--format json] | client-config <client>... [--dry-run|--remove] [--format json]"
        + " | install-addin [--variant <variant>]... [--format json] | uninstall-addin [--format json] | --version"
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
        ["install-addin", .. var options] => InstallAddinArguments(options),
        ["uninstall-addin", .. var options] => WithFormat(CliCommand.UninstallAddin, options),
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

    /// <summary>
    /// Variants and the format in any order; a variant named twice, in any case, counts once; none means every installed
    /// one. A variant is one argument without double quotes or control characters, as EPLAN's folder names are.
    /// </summary>
    private static CliArguments? InstallAddinArguments(string[] options)
    {
        var variants = new List<string>();
        bool json = false;
        for (int i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--format" when !json && i + 1 < options.Length && options[i + 1] == "json":
                    json = true;
                    i++;
                    break;
                case "--variant" when i + 1 < options.Length && IsVariant(options[i + 1]):
                    if (!variants.Contains(options[++i], StringComparer.OrdinalIgnoreCase))
                    {
                        variants.Add(options[i]);
                    }

                    break;
                default:
                    return null;
            }
        }

        return new(CliCommand.InstallAddin, json, Variants: variants);
    }

    private static bool IsVariant(string name) =>
        name.Length > 0 && name.Trim() == name && !name.StartsWith("--", StringComparison.Ordinal) && !name.Any(c => c == '"' || char.IsControl(c));
}
