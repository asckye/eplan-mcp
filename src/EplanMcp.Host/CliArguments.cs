using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Http;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Tools;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host;

internal enum CliCommand
{
    Serve,
    Doctor,
    AuditVerify,
    ClientConfig,
    InstallAddin,
    AddinVariants,
    UninstallAddin,
    Pack,
    VmKey,
    VmFirewall,
    Version,
}

/// <summary>
/// The command line of <c>eplanmcp</c>. Only doctor, audit verify, client-config, install-addin and uninstall-addin
/// take <c>--format json</c>, the output the Workbench reads (M5 design 4.4, 4.11); any other option or format is a
/// wrong command line. <see cref="ClientConfig"/> is set for client-config only, <see cref="Variants"/> for install-addin
/// only, <see cref="Pack"/> for pack only; <see cref="Profile"/> matters to serve only.
/// </summary>
internal sealed record CliArguments(
    CliCommand Command,
    bool Json,
    ClientConfigRequest? ClientConfig = null,
    ToolProfile Profile = ToolProfile.Full,
    IReadOnlyList<string>? Variants = null,
    PackRequest? Pack = null,
    HttpArguments? Http = null,
    string? KeyAction = null,
    VmFirewallRequest? Firewall = null)
{
    public static string Usage { get; } =
        "usage: eplanmcp [serve [--profile core|full]] | serve --http --address <IPv4> [--port <n>] [--process <pid>] | vm-key create|show|delete | doctor [--format json] | audit verify [--format json] | client-config <client>... [--dry-run|--remove] [--format json]"
        + " | client-config claude-code --plugin [--dry-run] [--format json] | addin-variants [--format json]"
        + " | install-addin [--variant <variant>]... [--format json] | uninstall-addin [--format json] | --version"
        + " | vm-firewall show|add|remove --port <n> --host-address <IPv4>"
        + Environment.NewLine + "       eplanmcp pack scan <[kind=|sample=]path>... [--id <id> --name <name> --version <semver> [--out <folder>] [--description <text>]"
        + " [--author <name>] [--eplan <range>] [--variant <variant>]... [--official]] [--bundle-projects] [--format json]"
        + " | pack check <folder|file.eplanpack> | pack export <folder> <file.eplanpack> | pack install <folder|file.eplanpack> (each [--format json])"
        + Environment.NewLine + "clients: " + string.Join(", ", McpClient.All.Select(c => c.Name));

    /// <summary>The command, or null for a wrong command line.</summary>
    public static CliArguments? Parse(string[] args) => args switch
    {
        [] or ["serve"] => new(CliCommand.Serve, Json: false),
        ["serve", "--profile", "full"] => new(CliCommand.Serve, Json: false),
        ["serve", "--profile", "core"] => new(CliCommand.Serve, Json: false, Profile: ToolProfile.Core),
        ["serve", .. var options] when HttpArguments.Parse(options) is { } http => new(CliCommand.Serve, Json: false, Http: http),
        ["vm-key", var action] when action is "create" or "show" or "delete" => new(CliCommand.VmKey, Json: false, KeyAction: action),
        ["vm-firewall", var action, .. var options] when VmFirewallRequest.Parse(action, options) is { } firewall => new(CliCommand.VmFirewall, Json: false, Firewall: firewall),
        ["--version"] => new(CliCommand.Version, Json: false),
        ["doctor", .. var options] => WithFormat(CliCommand.Doctor, options),
        ["audit", "verify", .. var options] => WithFormat(CliCommand.AuditVerify, options),
        ["client-config", .. var options] => ClientConfigArguments(options),
        ["install-addin", .. var options] => InstallAddinArguments(options),
        ["addin-variants", .. var options] => WithFormat(CliCommand.AddinVariants, options),
        ["uninstall-addin", .. var options] => WithFormat(CliCommand.UninstallAddin, options),
        ["pack", var verb, .. var options] => PackArguments(verb, options),
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
        bool json = false, dryRun = false, remove = false, plugin = false;
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
                case "--plugin" when !plugin:
                    plugin = true;
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
        return (dryRun && remove) || (clients.Count == 0 && !remove) || (plugin && (remove || clients.Count != 1 || clients[0].Name != "claude-code"))
            ? null : new(CliCommand.ClientConfig, json, new(clients, mode, plugin));
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

    /// <summary>
    /// The pack verb, its paths and options in any order, each option once (<c>--variant</c> may repeat). Only scan takes
    /// <c>--id</c>, <c>--name</c>, <c>--version</c> (all three or none) and, only with them, <c>--out</c>,
    /// <c>--description</c>, <c>--author</c>, <c>--eplan</c>, <c>--variant</c> and <c>--official</c>; a path never starts
    /// with <c>--</c>.
    /// </summary>
    private static CliArguments? PackArguments(string verb, string[] options)
    {
        PackAction? action = verb switch { "scan" => PackAction.Scan, "check" => PackAction.Check, "export" => PackAction.Export, "install" => PackAction.Install, _ => null };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new List<string>();
        var variants = new List<string>();
        bool json = false, official = false, bundle = false;
        for (int i = 0; i < options.Length; i++)
        {
            string option = options[i];
            bool hasValue = i + 1 < options.Length && options[i + 1].Length > 0 && !options[i + 1].StartsWith("--", StringComparison.Ordinal);
            if (option == "--format" && !json && hasValue && options[i + 1] == "json")
            {
                json = true;
                i++;
            }
            else if (action == PackAction.Scan && option == "--official" && !official)
            {
                official = true;
            }
            else if (action == PackAction.Scan && option == "--bundle-projects" && !bundle)
            {
                bundle = true;
            }
            else if (action == PackAction.Scan && option is "--id" or "--name" or "--version" or "--out" or "--description" or "--author" or "--eplan"
                && hasValue && values.TryAdd(option, options[i + 1]))
            {
                i++;
            }
            else if (action == PackAction.Scan && option == "--variant" && hasValue)
            {
                variants.Add(options[++i]);
            }
            else if (option.Length > 0 && !option.StartsWith("--", StringComparison.Ordinal))
            {
                paths.Add(option);
            }
            else
            {
                return null;
            }
        }

        int named = values.Keys.Count(k => k is "--id" or "--name" or "--version");
        PackIdentity? identity = named == 3
            ? new PackIdentity(values["--id"], values["--name"], values["--version"], values.GetValueOrDefault("--description"), values.GetValueOrDefault("--author"),
                values.GetValueOrDefault("--eplan"), variants)
            : null;
        bool valid = action switch
        {
            PackAction.Scan => paths.Count > 0 && named is 0 or 3 && (identity is not null || (values.Count == 0 && variants.Count == 0 && !official)),
            PackAction.Export => paths.Count == 2,
            null => false,
            _ => paths.Count == 1,
        };
        return valid ? new(CliCommand.Pack, json, Pack: new PackRequest(action!.Value, paths, values.GetValueOrDefault("--out"), identity, official, bundle)) : null;
    }

    private static bool IsVariant(string name) =>
        name.Length > 0 && name.Trim() == name && !name.StartsWith("--", StringComparison.Ordinal) && !name.Any(c => c == '"' || char.IsControl(c));
}
