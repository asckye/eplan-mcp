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
    public void Parse_AWrongCommandLine_IsNull(string line)
    {
        Assert.Null(CliArguments.Parse(Split(line)));
    }

    [Fact]
    public void Usage_NamesTheFormatOfBothCommands()
    {
        Assert.Equal("usage: eplanmcp [serve] | doctor [--format json] | audit verify [--format json] | --version", CliArguments.Usage);
    }

    private static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
