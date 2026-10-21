using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Doctor;

[Trait("Size", "Small")]
public sealed class DoctorTests
{
    [Fact]
    public void Format_FollowsTheUiLanguage()
    {
        DoctorCheck[] checks = [new("a", DoctorStatus.Failure, "中文", "English"), new("b", DoctorStatus.Ok, "好", "fine", "subject")];

        Assert.Equal("[FAIL] 中文" + Environment.NewLine + "[ OK ] 好" + Environment.NewLine, DoctorCommand.Format(checks, CultureInfo.GetCultureInfo("zh-CN")));
        Assert.Equal("[FAIL] English" + Environment.NewLine + "[ OK ] fine" + Environment.NewLine, DoctorCommand.Format(checks, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Theory]
    [InlineData(316, false)]
    [InlineData(DoctorChecks.UserPathWarningLength, false)]
    [InlineData(4604, true)]
    public void UserPathLength_WarnsAboveTheLimitSeenOnTheDevMachine(int length, bool warns)
    {
        DoctorCheck check = DoctorChecks.UserPathLength(length);

        Assert.Equal(warns ? DoctorStatus.Warning : DoctorStatus.Ok, check.Status);
        Assert.Contains(length.ToString(CultureInfo.InvariantCulture), check.English, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, false, "not redirected")]
    [InlineData(null, "Claude_pzs8sxrjxfjjc", true, "no package identity")]
    [InlineData("Claude_1.0_x64__pzs8sxrjxfjjc", null, true, "identity Claude_1.0_x64__pzs8sxrjxfjjc")]
    public void MsixEnvironment_ReportsRedirectionWithOrWithoutPackageIdentity(string? identity, string? package, bool redirected, string text)
    {
        // Spike S5: a Host the Claude desktop app starts has no package identity, yet its writes are redirected.
        DoctorCheck check = DoctorChecks.MsixEnvironment(identity, package);

        Assert.Equal(redirected ? DoctorStatus.Info : DoctorStatus.Ok, check.Status);
        Assert.Contains(text, check.English, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026.0.3.25421", false, "EPLAN 2026.0.3.25421: E")]
    [InlineData("2025.0.3.1", true, "in the supported range 2024-2027, not tested yet")]
    [InlineData("2023.0.3.19351", true, "outside the supported range 2024-2027")]
    public void Installation_SaysWhereTheVersionStandsInAdr0009(string version, bool flagged, string text)
    {
        DoctorCheck check = DoctorChecks.Installation(version, "E");

        Assert.Equal(flagged, check.Status != DoctorStatus.Ok);
        Assert.Contains(text, check.English, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_NoInstance_IsAFailureThatPointsAtThePerVariantRegistration()
    {
        IReadOnlyList<DoctorCheck> checks = await DoctorChecks.BridgeAsync(new FakeBridgeClient(), TestContext.Current.CancellationToken);

        DoctorCheck check = Assert.Single(checks);
        Assert.Equal(DoctorStatus.Failure, check.Status);
        Assert.Contains("variant", check.English, StringComparison.Ordinal);
        Assert.Contains("变体", check.Chinese, StringComparison.Ordinal);
        // Registering loads the add-in at once (api-notes A10); only a rebuilt add-in needs a restart (A8).
        Assert.DoesNotContain("restart", check.English, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bridge_HandshakeWithoutLicence_FailsTheLicenceCheck()
    {
        var bridge = new FakeBridgeClient
        {
            Status = new BridgeStatus(
                [new BridgeEndpoint("p", 7)],
                new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new EplanInstanceInfo { ProcessId = 7, Version = "2026.0.3", ApiExtensionLicence = false } },
                null),
        };

        IReadOnlyList<DoctorCheck> checks = await DoctorChecks.BridgeAsync(bridge, TestContext.Current.CancellationToken);

        Assert.Equal([DoctorStatus.Ok, DoctorStatus.Ok, DoctorStatus.Failure], checks.Select(c => c.Status));
        Assert.Contains("missing", checks[2].English, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_Problem_IsReportedWithItsReason()
    {
        var bridge = new FakeBridgeClient { Status = new BridgeStatus([new BridgeEndpoint("p", 7)], null, BridgeErrors.Incompatible(9)) };

        IReadOnlyList<DoctorCheck> checks = await DoctorChecks.BridgeAsync(bridge, TestContext.Current.CancellationToken);

        Assert.Equal(DoctorStatus.Failure, checks[^1].Status);
        Assert.Contains(ErrorReason.BridgeIncompatible, checks[^1].English, StringComparison.Ordinal);
    }

    [Fact]
    public void Installation_IsAboutItsVersion()
    {
        DoctorCheck check = DoctorChecks.Installation("2026.0.3.25421", "E");

        Assert.Equal((DoctorCheckIds.EplanInstallation, "2026.0.3.25421"), (check.Id, check.Subject));
    }

    [Fact]
    public void RunningEplan_IsAboutEachProcess()
    {
        DoctorCheck none = Assert.Single(DoctorChecks.RunningEplan([]));
        DoctorCheck[] running = [.. DoctorChecks.RunningEplan([7, 9])];

        Assert.Equal((DoctorCheckIds.EplanRunning, DoctorStatus.Warning, null), (none.Id, none.Status, none.Subject));
        Assert.Equal([(DoctorCheckIds.EplanRunning, "7"), (DoctorCheckIds.EplanRunning, "9")], running.Select(c => (c.Id, c.Subject)));
        Assert.All(running, c => Assert.Equal(DoctorStatus.Ok, c.Status));
    }

    [Fact]
    public async Task Bridge_ChecksAreAboutTheProcessOfEachPipeAndOfTheHandshake()
    {
        var bridge = new FakeBridgeClient
        {
            Status = new BridgeStatus(
                [new BridgeEndpoint("EplanMcp.Bridge.7.a1", 7), new BridgeEndpoint("EplanMcp.Bridge.9.b2", 9)],
                new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new EplanInstanceInfo { ProcessId = 9, Version = "2026.0.3", ApiExtensionLicence = true } },
                null),
        };

        IReadOnlyList<DoctorCheck> checks = await DoctorChecks.BridgeAsync(bridge, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                (DoctorCheckIds.BridgePipe, "7"),
                (DoctorCheckIds.BridgePipe, "9"),
                (DoctorCheckIds.BridgeHandshake, "9"),
                (DoctorCheckIds.BridgeLicence, "9"),
            ],
            checks.Select(c => (c.Id, c.Subject)));
    }

    [Fact]
    public async Task Bridge_WithoutAnInstanceOrHandshake_IsAboutNoParticularOne()
    {
        DoctorCheck none = Assert.Single(await DoctorChecks.BridgeAsync(new FakeBridgeClient(), TestContext.Current.CancellationToken));
        var bridge = new FakeBridgeClient { Status = new BridgeStatus([new BridgeEndpoint("p", 7)], null, BridgeErrors.Incompatible(9)) };
        DoctorCheck problem = (await DoctorChecks.BridgeAsync(bridge, TestContext.Current.CancellationToken))[^1];

        Assert.Equal((DoctorCheckIds.BridgePipe, null), (none.Id, none.Subject));
        Assert.Equal((DoctorCheckIds.BridgeHandshake, null), (problem.Id, problem.Subject));
    }

    [Fact]
    public void Backups_NoneYet_IsOnlyInformation()
    {
        var locations = new HostDataLocations(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        DoctorCheck check = DoctorChecks.Backups(locations);

        Assert.Equal(DoctorStatus.Info, check.Status);
        Assert.Contains("No backups yet", check.English, StringComparison.Ordinal);
    }

    [Fact]
    public void EplanInstallations_NothingInstalled_IsAFailure()
    {
        DoctorCheck check = Assert.Single(DoctorChecks.EplanInstallations(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.Equal(DoctorStatus.Failure, check.Status);
    }
}
