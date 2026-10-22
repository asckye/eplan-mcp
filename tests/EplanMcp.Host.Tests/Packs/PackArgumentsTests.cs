using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class PackArgumentsTests
{
    [Fact]
    public void Parse_ScanWithIdentityAndOptions_GivesTheRequest()
    {
        CliArguments cli = CliArguments.Parse(
            ["pack", "scan", "rule=rules", "D:\\in", "--id", "acme.base", "--name", "ACME base", "--version", "1.0.0", "--out", "D:\\out",
             "--variant", "Electric P8", "--variant", "Pro Panel", "--eplan", ">=2026.0", "--author", "Wang", "--official", "--format", "json"])!;

        Assert.Equal((CliCommand.Pack, true), (cli.Command, cli.Json));
        PackRequest request = cli.Pack!;
        Assert.Equal((PackAction.Scan, "D:\\out", true), (request.Action, request.Out, request.Official));
        Assert.Equal(["rule=rules", "D:\\in"], request.Paths);
        Assert.Equal(("acme.base", "ACME base", "1.0.0", ">=2026.0", "Wang"), (request.Identity!.Id, request.Identity.Name, request.Identity.Version, request.Identity.EplanVersions, request.Identity.Author));
        Assert.Equal(["Electric P8", "Pro Panel"], request.Identity.Variants!);
    }

    [Theory]
    [InlineData("pack scan a b", "Scan", 2, false)]
    [InlineData("pack check D:\\pack", "Check", 1, false)]
    [InlineData("pack check a.eplanpack --format json", "Check", 1, true)]
    [InlineData("pack export D:\\pack a.eplanpack", "Export", 2, false)]
    [InlineData("pack install a.eplanpack --format json", "Install", 1, true)]
    public void Parse_PackVerbs_GiveTheirPaths(string line, string action, int paths, bool json)
    {
        CliArguments cli = CliArguments.Parse(line.Split(' '))!;

        Assert.Equal((Enum.Parse<PackAction>(action), paths, json, null), (cli.Pack!.Action, cli.Pack.Paths.Count, cli.Json, cli.Pack.Identity));
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("pack scan")]
    [InlineData("pack scan --format json")]
    [InlineData("pack scan a --out o")]
    [InlineData("pack scan a --official")]
    [InlineData("pack scan a --variant x")]
    [InlineData("pack scan a --id x")]
    [InlineData("pack scan a --id x --name n")]
    [InlineData("pack scan a --id x --id y --name n --version 1.0.0")]
    [InlineData("pack scan a --id --name n --version 1.0.0")]
    [InlineData("pack check")]
    [InlineData("pack check a b")]
    [InlineData("pack check a --format xml")]
    [InlineData("pack export a")]
    [InlineData("pack install a --out o")]
    [InlineData("pack install a --id x --name n --version 1.0.0")]
    [InlineData("pack remove a")]
    [InlineData("pack Scan a")]
    public void Parse_WrongPackCommandLine_IsNull(string line)
    {
        Assert.Null(CliArguments.Parse(line.Split(' ')));
    }

    [Theory]
    [InlineData("rule=D:\\rules", "D:\\rules", ResourceKind.Rule, false)]
    [InlineData("material_mapping=parts.json", "parts.json", ResourceKind.MaterialMapping, false)]
    [InlineData("sample=D:\\Plant.zw1", "D:\\Plant.zw1", null, true)]
    [InlineData("D:\\a=b\\c", "D:\\a=b\\c", null, false)]
    [InlineData("rules=x", "rules=x", null, false)]
    public void Input_PrefixNamesTheKindOrASample(string argument, string path, ResourceKind? kind, bool sample)
    {
        Assert.Equal(new EplanMcp.Packs.Scanning.ScanInput(path, kind, sample, Official: true), PackCommand.Input(argument, official: true));
    }
}
