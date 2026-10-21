using EplanMcp.Host.ClientConfig;

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
    public void Parse_AKnownCommandLine_GivesTheCommandAndFormat(string line, string command, bool json)
    {
        Assert.Equal(new CliArguments(Enum.Parse<CliCommand>(command), json), CliArguments.Parse(Split(line)));
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

    [Fact]
    public void Parse_EveryClientName_IsAccepted()
    {
        Assert.All(McpClient.All, c => Assert.Equal([c], CliArguments.Parse(["client-config", c.Name])!.ClientConfig!.Clients));
    }

    [Fact]
    public void Usage_NamesEveryCommandWithItsOptionsAndTheClients()
    {
        Assert.Equal(
            "usage: eplanmcp [serve] | doctor [--format json] | audit verify [--format json] | client-config <client>... [--dry-run|--remove] [--format json] | --version"
            + Environment.NewLine
            + "clients: claude-code, claude-desktop, codex, gemini-cli, qwen-code, kimi-code, codebuddy, opencode, qwen-agent, cursor, vscode-copilot",
            CliArguments.Usage);
    }

    private static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
