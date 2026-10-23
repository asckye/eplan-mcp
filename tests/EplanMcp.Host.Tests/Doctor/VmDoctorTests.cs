using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Http;
using EplanMcp.Host.Tests.Http;

namespace EplanMcp.Host.Tests.Doctor;

[Trait("Size", "Small")]
public sealed class VmDoctorTests
{
    private static readonly DateTimeOffset _start = DateTimeOffset.UnixEpoch.AddDays(1);

    [Fact]
    public void MissingStateAndRule_AreInformationalWithExistingJsonShape()
    {
        var firewall = new FakeVmFirewall();
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => [], _ => throw new InvalidOperationException("No PID to check."), firewall);

        Assert.Equal([DoctorCheckIds.HttpService, DoctorCheckIds.VmFirewall], checks.Select(c => c.Id));
        Assert.All(checks, check => Assert.Equal(DoctorStatus.Info, check.Status));
        Assert.Equal(1, firewall.Reads);
        Assert.Contains("not running", checks[0].English, StringComparison.Ordinal);
        Assert.Contains("absent", checks[1].English, StringComparison.Ordinal);
        JsonObject json = JsonNode.Parse(DoctorCommand.FormatJson("1.2.3", checks))!.AsObject();
        Assert.Equal(["schemaVersion", "hostVersion", "status", "checks"], json.Select(p => p.Key));
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal("ok", json["status"]!.GetValue<string>());
        Assert.All(json["checks"]!.AsArray(), check => Assert.Equal(["id", "status", "zh", "en"], check!.AsObject().Select(p => p.Key)));
    }

    [Fact]
    public void RunningState_ReportsBoundEndpointAndActualFirewallScopeInTextAndJson()
    {
        HttpServiceState state = HttpServiceStateStore.Parse("""{"processId":7,"processStart":"1970-01-02T00:00:00+00:00","address":"192.168.56.2","port":8766}""");
        VmFirewallRule rule = new VmFirewallRequest("show", 8766, "192.168.56.1").Definition(VmFirewallTests.ProgramPath);
        var firewall = new FakeVmFirewall { Rules = [rule] };
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => [state], pid => pid == 7 ? _start : null, firewall);

        Assert.Equal(DoctorStatus.Ok, checks[0].Status);
        Assert.Contains("192.168.56.2:8766 (PID 7)", checks[0].English, StringComparison.Ordinal);
        foreach (CultureInfo culture in new[] { CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("zh-CN") })
        {
            string text = DoctorCommand.Format(checks, culture);
            Assert.Contains("192.168.56.2:8766", text, StringComparison.Ordinal);
            Assert.Contains(rule.Describe(), text, StringComparison.Ordinal);
        }

        JsonNode json = JsonNode.Parse(DoctorCommand.FormatJson("1.2.3", checks))!;
        Assert.Equal(checks.Select(c => c.Id), json["checks"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()));
        Assert.Equal(checks[1].English, json["checks"]![1]!["en"]!.GetValue<string>());
        Assert.Equal(1, firewall.Reads);
    }

    [Fact]
    public void ExitedOrReusedPid_IsNotReportedAsRunning()
    {
        HttpServiceState[] stale = [new(7, _start, "192.168.56.2", 8766), new(9, _start, "192.168.56.3", 9000)];
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => stale, pid => pid == 7 ? null : _start.AddSeconds(1), new FakeVmFirewall());

        Assert.Equal(DoctorStatus.Info, checks[0].Status);
        Assert.Contains("not running", checks[0].English, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.", checks[0].English, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleServicesInTheSameRoot_AreAllReported()
    {
        HttpServiceState[] states = [new(7, _start, "192.168.56.2", 8766), new(9, _start, "192.168.56.3", 9000)];
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => states, _ => _start, new FakeVmFirewall());

        Assert.Equal(3, checks.Count);
        Assert.All(checks.Take(2), c => Assert.Equal(DoctorStatus.Ok, c.Status));
        Assert.Contains("192.168.56.3:9000", checks[1].English, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"processId":7,"processStart":"1970-01-02T00:00:00Z","address":"0.0.0.0","port":8766}""")]
    [InlineData("""{"processId":7,"processStart":"1970-01-02T00:00:00Z","address":"192.168.56.2","port":0}""")]
    public void InvalidState_WarnsAndStillReadsFirewall(string json)
    {
        var firewall = new FakeVmFirewall();
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => [HttpServiceStateStore.Parse(json)], _ => _start, firewall);

        Assert.Throws<JsonException>(() => HttpServiceStateStore.Parse(json));
        Assert.Equal([DoctorStatus.Warning, DoctorStatus.Info], checks.Select(c => c.Status));
        Assert.Equal(1, firewall.Reads);
    }

    [Fact]
    public void UnreadableStateOrFirewall_ReportsUnknownInsteadOfAbsent()
    {
        var firewall = new FakeVmFirewall { Problem = new InvalidOperationException("Unavailable") };
        IReadOnlyList<DoctorCheck> checks = VmDoctorChecks.Read(() => throw new UnauthorizedAccessException(), _ => null, firewall);

        Assert.All(checks, c => Assert.Equal(DoctorStatus.Warning, c.Status));
        Assert.DoesNotContain(checks, c => c.English.Contains("not running", StringComparison.Ordinal) || c.English.Contains("absent", StringComparison.Ordinal));
        Assert.Contains("warning", DoctorCommand.FormatJson("1.2.3", checks), StringComparison.Ordinal);
    }
}
