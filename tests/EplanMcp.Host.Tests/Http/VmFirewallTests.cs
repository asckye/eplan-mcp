using System.Globalization;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class VmFirewallTests
{
    internal const string ProgramPath = @"C:\Program Files\EplanMcp\eplanmcp.exe";

    [Theory]
    [InlineData("show", "1")]
    [InlineData("add", "8766")]
    [InlineData("remove", "65535")]
    public void Parse_AcceptsVerbsBoundariesAndEitherOptionOrder(string action, string port)
    {
        foreach (string[] options in new[]
        {
            new[] { "--port", port, "--host-address", "192.168.56.1" },
            new[] { "--host-address", "192.168.56.1", "--port", port },
        })
        {
            CliArguments cli = Assert.IsType<CliArguments>(CliArguments.Parse(["vm-firewall", action, .. options]));
            Assert.Equal(CliCommand.VmFirewall, cli.Command);
            Assert.False(cli.Json);
            Assert.Equal(new VmFirewallRequest(action, int.Parse(port, CultureInfo.InvariantCulture), "192.168.56.1"), cli.Firewall);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+8766")]
    [InlineData(" 8766")]
    [InlineData("8766 ")]
    [InlineData("8.766")]
    [InlineData("8766;whoami")]
    [InlineData("999999999999")]
    public void Parse_RejectsInvalidPort(string port) => Assert.Null(CliArguments.Parse(["vm-firewall", "add", "--port", port, "--host-address", "192.168.56.1"]));

    [Theory]
    [InlineData("*")]
    [InlineData("LocalSubnet")]
    [InlineData("192.168.56.0/24")]
    [InlineData("192.168.56.1,192.168.56.2")]
    [InlineData("192.168.56.1-192.168.56.2")]
    [InlineData("192.168.056.1")]
    [InlineData("127.1")]
    [InlineData("0x7f000001")]
    [InlineData("192.168.56.256")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.56.1")]
    [InlineData("192.168.56.1 ")]
    [InlineData("192.168.56.1;whoami")]
    [InlineData("")]
    public void Parse_RejectsAnythingExceptOneCanonicalUnicastIPv4(string address) => Assert.Null(CliArguments.Parse(["vm-firewall", "remove", "--port", "8766", "--host-address", address]));

    [Fact]
    public void Parse_RejectsMissingDuplicateUnknownAndExtraArguments()
    {
        string[] valid = ["--port", "8766", "--host-address", "192.168.56.1"];
        Assert.Null(CliArguments.Parse(["vm-firewall"]));
        Assert.Null(CliArguments.Parse(["vm-firewall", "show"]));
        Assert.Null(CliArguments.Parse(["vm-firewall", "enable", .. valid]));
        Assert.Null(CliArguments.Parse(["vm-firewall", "add", "--port", "8766"]));
        Assert.Null(CliArguments.Parse(["vm-firewall", "remove", "--host-address", "192.168.56.1"]));
        foreach (string[] extra in new[]
        {
            new[] { "--port", "8766" }, new[] { "--host-address", "192.168.56.1" },
            new[] { "--format", "json" }, new[] { "--program", ProgramPath }, new[] { "--host-address" }, new[] { "extra" },
        })
        {
            Assert.Null(CliArguments.Parse(["vm-firewall", "show", .. valid, .. extra]));
        }
    }

    [Fact]
    public void Definition_RestrictsAllFieldsAsRequiredByAdr0018()
    {
        VmFirewallRule rule = new VmFirewallRequest("add", 8766, "192.168.56.1").Definition(ProgramPath);

        Assert.Equal("EplanMcp.Vm.Http", rule.Name);
        Assert.Equal(6, rule.Protocol);
        Assert.Equal("8766", rule.Port);
        Assert.Equal(ProgramPath, rule.Program);
        Assert.Equal("192.168.56.1", rule.RemoteAddress);
        Assert.Equal(3, rule.Profiles);
        Assert.Equal(1, rule.Direction);
        Assert.Equal(1, rule.Action);
        Assert.True(rule.Enabled);
        Assert.False(rule.EdgeTraversal);
    }

    [Theory]
    [InlineData("eplanmcp.exe")]
    [InlineData(@"C:\app\dotnet.exe")]
    [InlineData(@"C:\app\eplanmcp.dll")]
    public void Definition_RefusesAnIncorrectExecutable(string path) => Assert.Throws<InvalidOperationException>(() => new VmFirewallRequest("add", 8766, "192.168.56.1").Definition(path));

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public void Run_Unelevated_RefusesBeforeReadingFirewallOrExecutable(string action)
    {
        var firewall = new FakeVmFirewall();
        var command = new VmFirewallCommand(firewall, () => false, () => throw new InvalidOperationException("Must not resolve the executable."));
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(1, command.Run(new(action, 8766, "192.168.56.1"), output));
        Assert.Contains("Administrator elevation is required", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, firewall.Reads);
    }

    [Fact]
    public void Show_Unelevated_ReadsActualRuleAndDoesNotCheckElevation()
    {
        VmFirewallRule actual = new VmFirewallRequest("add", 9000, "10.0.0.1").Definition(ProgramPath) with { Profiles = 7, Enabled = false };
        var firewall = new FakeVmFirewall { Rules = [actual] };
        var command = new VmFirewallCommand(firewall, () => throw new InvalidOperationException("Show must not check elevation."),
            () => throw new InvalidOperationException("Show must not resolve the executable."));
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, command.Run(new("show", 8766, "192.168.56.1"), output));
        Assert.Equal(actual.Describe() + Environment.NewLine, output.ToString());
        Assert.Equal(1, firewall.Reads);
    }

    [Fact]
    public void Matches_HandlesWindowsHostMaskAndPathCaseButRejectsBroaderRules()
    {
        VmFirewallRule expected = new VmFirewallRequest("add", 8766, "192.168.56.1").Definition(ProgramPath);
        Assert.True((expected with { Program = ProgramPath.ToUpperInvariant(), RemoteAddress = "192.168.56.1/255.255.255.255" }).Matches(expected));
        Assert.True((expected with { RemoteAddress = "192.168.56.1/32" }).Matches(expected));
        foreach (VmFirewallRule changed in new[]
        {
            expected with { Name = "other" }, expected with { Protocol = 17 }, expected with { Port = "8766,9000" },
            expected with { Program = @"C:\other\eplanmcp.exe" }, expected with { RemoteAddress = "*" }, expected with { Profiles = 7 },
            expected with { Direction = 2 }, expected with { Action = 0 }, expected with { Enabled = false }, expected with { EdgeTraversal = true },
        })
        {
            Assert.False(changed.Matches(expected));
        }
    }
}
