using System.Globalization;
using System.Text;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Doctor;

/// <summary><c>eplanmcp doctor</c>: prints every check and exits 1 when any failed. Never used in serve mode.</summary>
internal static class DoctorCommand
{
    public static async Task<int> RunAsync(TextWriter output, CultureInfo culture, CancellationToken cancellationToken)
    {
        HostDataLocations locations = HostDataLocations.FromEnvironment();
        var checks = new List<DoctorCheck>
        {
            DoctorChecks.HostVersion(),
            DoctorChecks.DataDirectory(locations),
            DoctorChecks.Configuration(locations),
            DoctorChecks.MsixEnvironment(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        };
        checks.AddRange(DoctorChecks.EplanInstallations(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        checks.AddRange(DoctorChecks.RunningEplan());
        var bridge = new BridgeClient(new PipeBridgeDiscovery(), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        await using (bridge.ConfigureAwait(false))
        {
            checks.AddRange(await DoctorChecks.BridgeAsync(bridge, cancellationToken).ConfigureAwait(false));
        }

        checks.Add(DoctorChecks.UserPathLength(DoctorChecks.ReadUserPathLength()));

        await output.WriteAsync(Format(checks, culture)).ConfigureAwait(false);
        return checks.Any(c => c.Status == DoctorStatus.Failure) ? 1 : 0;
    }

    internal static string Format(IEnumerable<DoctorCheck> checks, CultureInfo culture)
    {
        bool chinese = culture.TwoLetterISOLanguageName == "zh";
        var text = new StringBuilder();
        foreach (DoctorCheck check in checks)
        {
            string tag = check.Status switch
            {
                DoctorStatus.Ok => "[ OK ]",
                DoctorStatus.Info => "[INFO]",
                DoctorStatus.Warning => "[WARN]",
                _ => "[FAIL]",
            };
            text.Append(tag).Append(' ').AppendLine(chinese ? check.Chinese : check.English);
        }

        return text.ToString();
    }
}
