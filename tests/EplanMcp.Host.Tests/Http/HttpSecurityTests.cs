using System.Net;
using EplanMcp.Host.Http;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Primitives;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class HttpSecurityTests
{
    [Fact]
    public void OriginAllowed_DefaultEmptyAndExactAllowList_RejectsForeignNullEmptyAndMultiple()
    {
        var empty = new HttpGate([]);
        var configured = new HttpGate(["http://trusted.example"]);
        Assert.True(empty.OriginAllowed(StringValues.Empty));
        Assert.True(configured.OriginAllowed("http://trusted.example"));
        foreach (string origin in new[] { "http://trusted.example", "null", "", "http://foreign.example" })
        {
            Assert.False(empty.OriginAllowed(origin));
        }

        Assert.False(configured.OriginAllowed("http://TRUSTED.example"));
        Assert.False(configured.OriginAllowed(new StringValues(["http://trusted.example", "http://foreign.example"])));
    }

    [Fact]
    public void HostAllowed_OnlyTheBoundAddressAndPort()
    {
        IPAddress bound = IPAddress.Parse("192.168.56.1");
        Assert.True(HttpGate.HostAllowed("192.168.56.1:8765", bound, 8765));
        Assert.True(HttpGate.HostAllowed("192.168.56.1", bound, 80));
        foreach (string host in new[] { "attacker.example:8765", "localhost:8765", "192.168.56.1", "192.168.56.1:8766", "192.168.56.2:8765", "" })
        {
            Assert.False(HttpGate.HostAllowed(host, bound, 8765));
        }

        Assert.False(HttpGate.HostAllowed(StringValues.Empty, bound, 8765));
        Assert.False(HttpGate.HostAllowed(new StringValues(["192.168.56.1:8765", "192.168.56.1:8765"]), bound, 8765));
    }

    [Fact]
    public void Check_AllowsKeyFreeRequestsFromAllowedOriginsAndRefusesForeignOrigins()
    {
        var gate = new HttpGate(["http://trusted.example"]);
        Assert.Equal(0, gate.Check(StringValues.Empty));
        Assert.Equal(0, gate.Check("http://trusted.example"));
        Assert.Equal(403, gate.Check("http://foreign.example"));
        Assert.Equal(403, gate.Check("null"));
    }

    [Theory]
    [InlineData("serve --http --address localhost")]
    [InlineData("serve --http --address eplan.example")]
    [InlineData("serve --http --address 0.0.0.0")]
    [InlineData("serve --http --address ::1")]
    [InlineData("serve --http --address ::ffff:127.0.0.1")]
    [InlineData("serve --http --address 127.1")]
    [InlineData("serve --http --address 0177.0.0.1")]
    [InlineData("serve --http --address 0x7f.0.0.1")]
    [InlineData("serve --http --address 127.0.0.256")]
    [InlineData("serve --http")]
    [InlineData("serve --address 127.0.0.1")]
    [InlineData("serve --http --address 127.0.0.1 --port 0")]
    [InlineData("serve --http --address 127.0.0.1 --port 65536")]
    [InlineData("serve --http --address 127.0.0.1 --port -1")]
    [InlineData("serve --http --address 127.0.0.1 --process 0")]
    [InlineData("serve --http --address 127.0.0.1 --process -1")]
    [InlineData("serve --http --address 127.0.0.1 --http")]
    [InlineData("serve --http --address 127.0.0.1 --port 8766 --port 8766")]
    [InlineData("serve --http --address 127.0.0.1 --profile core")]
    public void Parse_UnsafeOrInvalidHttpArguments_AreRefused(string line) => Assert.Null(CliArguments.Parse(line.Split(' ')));

    [Fact]
    public void Parse_ExplicitIPv4_DefaultsAndReorderedOptions()
    {
        Assert.Equal(new HttpArguments(IPAddress.Loopback, 8766, null), CliArguments.Parse(["serve", "--http", "--address", "127.0.0.1"])!.Http);
        Assert.Equal(new HttpArguments(IPAddress.Parse("192.168.1.2"), 9000, 42), CliArguments.Parse(["serve", "--process", "42", "--address", "192.168.1.2", "--http", "--port", "9000"])!.Http);
    }

    [Fact]
    public void Configuration_OriginsDefaultEmptyAndCanBeSetInConfigJson()
    {
        var locations = new HostDataLocations(@"C:\fake");
        Assert.Empty(HostConfig.Defaults(locations).HttpAllowedOrigins);
        (HostConfig? config, string? error) = HostConfig.Parse("""{"httpAllowedOrigins":["http://trusted.example"]}""", locations);
        Assert.Null(error);
        Assert.Equal(["http://trusted.example"], config!.HttpAllowedOrigins);
    }
}
