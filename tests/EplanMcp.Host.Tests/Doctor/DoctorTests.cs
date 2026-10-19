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
        DoctorCheck[] checks = [new(DoctorStatus.Failure, "中文", "English"), new(DoctorStatus.Ok, "好", "fine")];

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
    public void EplanInstallations_NothingInstalled_IsAFailure()
    {
        DoctorCheck check = Assert.Single(DoctorChecks.EplanInstallations(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        Assert.Equal(DoctorStatus.Failure, check.Status);
    }
}
