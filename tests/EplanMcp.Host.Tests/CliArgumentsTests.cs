using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests;

[Trait("Size", "Small")]
public sealed class CliArgumentsTests
{
    [Theory]
    [InlineData("", "Serve", false)]
    [InlineData("serve", "Serve", false)]
    [InlineData("--version", "Version", false)]
    [InlineData("doctor", "Doctor", false)]
    [InlineData("doctor --format json", "Doctor", true)]
    [InlineData("audit verify", "AuditVerify", false)]
    [InlineData("audit verify --format json", "AuditVerify", true)]
    [InlineData("uninstall-addin", "UninstallAddin", false)]
    [InlineData("uninstall-addin --format json", "UninstallAddin", true)]
    public void Parse_AKnownCommandLine_GivesTheCommandAndFormat(string line, string command, bool json)
    {
        Assert.Equal(new CliArguments(Enum.Parse<CliCommand>(command), json), CliArguments.Parse(Split(line)));
    }

    [Theory]
    [InlineData("serve --profile core", "Core")]
    [InlineData("serve --profile full", "Full")]
    public void Parse_ServeWithAProfile_GivesTheProfile(string line, string profile)
    {
        Assert.Equal(new CliArguments(CliCommand.Serve, Json: false, Profile: Enum.Parse<ToolProfile>(profile)), CliArguments.Parse(Split(line)));
    }

    [Theory]
    [InlineData("serve --profile")]
    [InlineData("serve --profile Core")]
    [InlineData("serve --profile minimal")]
    [InlineData("serve --profile core --profile core")]
    [InlineData("--profile core")]
    [InlineData("doctor --profile core")]
    public void Parse_AWrongProfile_IsNull(string line)
    {
        Assert.Null(CliArguments.Parse(Split(line)));
    }

    [Theory]
    [InlineData("doctor --format")]
    [InlineData("doctor --format xml")]
    [InlineData("doctor --format text")]
    [InlineData("doctor --format JSON")]
    [InlineData("doctor --format json --format json")]
    [InlineData("doctor --format json extra")]
    [InlineData("doctor json")]
    [InlineData("doctor --json")]
    [InlineData("audit verify --format")]
    [InlineData("audit verify --format yaml")]
    [InlineData("audit --format json")]
    [InlineData("audit")]
    [InlineData("verify")]
    [InlineData("serve --format json")]
    [InlineData("--version --format json")]
    [InlineData("--format json")]
    [InlineData("client-config")]
    [InlineData("client-config --dry-run")]
    [InlineData("client-config --format json")]
    [InlineData("client-config codex --dry-run --remove")]
    [InlineData("client-config codex --dry-run --dry-run")]
    [InlineData("client-config codex --remove --remove")]
    [InlineData("client-config codex --format")]
    [InlineData("client-config codex --format text")]
    [InlineData("client-config codex --format json --format json")]
    [InlineData("client-config Codex")]
    [InlineData("client-config vscode")]
    [InlineData("client-config codex --all")]
    [InlineData("client-config codex -n")]
    [InlineData("install-addin --variant")]
    [InlineData("install-addin --variant --format json")]
    [InlineData("install-addin Electric")]
    [InlineData("install-addin --format json --format json")]
    [InlineData("install-addin --format text")]
    [InlineData("install-addin --all")]
    [InlineData("uninstall-addin --all")]
    [InlineData("uninstall-addin --variant Electric")]
    [InlineData("uninstall-addin --format")]
    [InlineData("install")]
    public void Parse_AWrongCommandLine_IsNull(string line)
    {
        Assert.Null(CliArguments.Parse(Split(line)));
    }

    [Theory]
    [InlineData("client-config codex", "codex", "Write", false)]
    [InlineData("client-config codex cursor claude-desktop", "codex cursor claude-desktop", "Write", false)]
    [InlineData("client-config codex codex cursor codex", "codex cursor", "Write", false)]
    [InlineData("client-config --dry-run codex --format json", "codex", "DryRun", true)]
    [InlineData("client-config codex --format json --dry-run gemini-cli", "codex gemini-cli", "DryRun", true)]
    [InlineData("client-config vscode-copilot --remove", "vscode-copilot", "Remove", false)]
    [InlineData("client-config --remove", "", "Remove", false)]
    [InlineData("client-config --remove --format json", "", "Remove", true)]
    public void Parse_ClientConfig_GivesTheClientsInOrderAndTheMode(string line, string clients, string mode, bool json)
    {
        CliArguments cli = CliArguments.Parse(Split(line))!;

        Assert.Equal((CliCommand.ClientConfig, json), (cli.Command, cli.Json));
        Assert.Equal(Split(clients), cli.ClientConfig!.Clients.Select(c => c.Name));
        Assert.Equal(Enum.Parse<ClientConfigMode>(mode), cli.ClientConfig.Mode);
    }

    [Theory]
    [InlineData(new string[] { "install-addin" }, new string[0], false)]
    [InlineData(new[] { "install-addin", "--variant", "Electric P8" }, new[] { "Electric P8" }, false)]
    [InlineData(new[] { "install-addin", "--format", "json", "--variant", "Pro Panel", "--variant", "Electric P8" }, new[] { "Pro Panel", "Electric P8" }, true)]
    [InlineData(new[] { "install-addin", "--variant", "Pro Panel", "--format", "json", "--variant", "pro panel" }, new[] { "Pro Panel" }, true)]
    public void Parse_InstallAddin_GivesTheVariantsInOrder(string[] args, string[] variants, bool json)
    {
        CliArguments cli = CliArguments.Parse(args)!;

        Assert.Equal((CliCommand.InstallAddin, json), (cli.Command, cli.Json));
        Assert.Equal(variants, cli.Variants);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Pro Panel")]
    [InlineData("Pro Panel ")]
    [InlineData("--format")]
    [InlineData("Pro\" EplApiModuleAction /register:\"C:\\x.dll")]
    [InlineData("Pro\nPanel")]
    public void Parse_InstallAddinWithAVariantThatCannotBeAFolderName_IsNull(string variant)
    {
        // The variant goes inside /Variant:"..." on EPLAN's command line; a quote would end it early.
        Assert.Null(CliArguments.Parse(["install-addin", "--variant", variant]));
    }

    [Fact]
    public void Parse_EveryClientName_IsAccepted()
    {
        Assert.All(McpClient.All, c => Assert.Equal([c], CliArguments.Parse(["client-config", c.Name])!.ClientConfig!.Clients));
    }

    [Fact]
    public void Usage_NamesEveryCommandWithItsOptionsAndTheClients()
    {
        Assert.Equal(
            "usage: eplanmcp [serve [--profile core|full]] | doctor [--format json] | audit verify [--format json] | client-config <client>... [--dry-run|--remove] [--format json]"
            + " | install-addin [--variant <variant>]... [--format json] | uninstall-addin [--format json] | --version"
            + Environment.NewLine
            + "       eplanmcp pack scan <[kind=|sample=]path>... [--id <id> --name <name> --version <semver> [--out <folder>] [--description <text>]"
            + " [--author <name>] [--eplan <range>] [--variant <variant>]... [--official]] [--format json]"
            + " | pack check <folder|file.eplanpack> | pack export <folder> <file.eplanpack> | pack install <folder|file.eplanpack> (each [--format json])"
            + Environment.NewLine
            + "clients: claude-code, claude-desktop, codex, gemini-cli, qwen-code, kimi-code, codebuddy, opencode, qwen-agent, cursor, vscode-copilot",
            CliArguments.Usage);
    }

    private static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
