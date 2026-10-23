using System.Net;
using EplanMcp.Host.Http;
using EplanMcp.Host.Safety;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Small")]
public sealed class HttpSecurityTests
{
    [Fact]
    public void Generate_Produces256BitsInBase64UrlAndIndependentKeys()
    {
        string key = VmKey.Generate();
        Assert.Equal(43, key.Length);
        Assert.All(key, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.Equal(32, Convert.FromBase64String(key.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.NotEqual(key, VmKey.Generate());
    }

    [Fact]
    public void KeyMatches_RequiresExactlyOneExactBearerValue()
    {
        string key = VmKey.Generate();
        Assert.True(HttpGate.KeyMatches("Bearer " + key, key));
        foreach (string wrong in new[] { "", key, "bearer " + key, "Bearer " + VmKey.Generate(), "Bearer " + key + " ", "Bearer " + key[..^1] })
        {
            Assert.False(HttpGate.KeyMatches(wrong, key));
        }

        Assert.False(HttpGate.KeyMatches(StringValues.Empty, key));
        Assert.False(HttpGate.KeyMatches(new StringValues(["Bearer " + key, "Bearer " + key]), key));
        Assert.False(HttpGate.KeyMatches("Bearer ", null));
    }

    [Fact]
    public void OriginAllowed_DefaultEmptyAndExactAllowList_RejectsForeignNullEmptyAndMultiple()
    {
        var empty = new HttpGate([], TimeProvider.System, NullLogger<HttpGate>.Instance);
        var configured = new HttpGate(["http://trusted.example"], TimeProvider.System, NullLogger<HttpGate>.Instance);
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
    public void Check_TenthFailureBlocksAllRequestsForAMinute_Only401Counts()
    {
        var clock = new Clock();
        var gate = new HttpGate([], clock, NullLogger<HttpGate>.Instance);
        string key = VmKey.Generate();
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(403, gate.Check(IPAddress.Loopback, "http://foreign.example", "wrong", key));
            Assert.Equal(401, gate.Check(IPAddress.Loopback, StringValues.Empty, "wrong", key));
        }

        Assert.Equal(429, gate.Check(IPAddress.Loopback, StringValues.Empty, "Bearer " + key, key));
        Assert.Equal(0, gate.Check(IPAddress.Parse("127.0.0.2"), StringValues.Empty, "Bearer " + key, key));
        clock.Now = clock.Now.AddSeconds(59);
        Assert.Equal(429, gate.Check(IPAddress.Loopback, "http://foreign.example", "Bearer " + key, key));
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Equal(0, gate.Check(IPAddress.Loopback, StringValues.Empty, "Bearer " + key, key));
    }

    [Fact]
    public void Check_FailuresOutsideSlidingWindowDoNotAccumulate()
    {
        var clock = new Clock();
        var gate = new HttpGate([], clock, NullLogger<HttpGate>.Instance);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(401, gate.Check(IPAddress.Loopback, StringValues.Empty, "wrong", "key"));
            clock.Now = clock.Now.AddSeconds(7);
        }
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
    [InlineData("vm-key rotate")]
    [InlineData("vm-key show --format json")]
    public void Parse_UnsafeOrInvalidHttpArguments_AreRefused(string line) => Assert.Null(CliArguments.Parse(line.Split(' ')));

    [Theory]
    [InlineData("create")]
    [InlineData("show")]
    [InlineData("delete")]
    public void Parse_KeyCommands_AreRecognized(string action) => Assert.Equal(action, CliArguments.Parse(["vm-key", action])!.KeyAction);

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

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
