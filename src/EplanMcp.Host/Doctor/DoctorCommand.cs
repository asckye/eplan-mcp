using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Doctor;

/// <summary><c>eplanmcp doctor</c>: prints every check and exits 1 when any failed. Never used in serve mode.</summary>
internal static class DoctorCommand
{
    /// <summary>The version of the JSON shape (architecture 5.2); a change to the shape raises it.</summary>
    public const int SchemaVersion = 1;

    public static async Task<int> RunAsync(TextWriter output, CultureInfo culture, bool json, CancellationToken cancellationToken)
    {
        HostDataLocations locations = HostDataLocations.FromEnvironment();
        var checks = new List<DoctorCheck>
        {
            DoctorChecks.HostVersion(),
            DoctorChecks.DataDirectory(locations),
            DoctorChecks.Configuration(locations),
            DoctorChecks.AuditDirectory(locations),
            DoctorChecks.Exports(locations),
            DoctorChecks.Backups(locations),
            DoctorChecks.MsixEnvironment(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        };
        checks.AddRange(DoctorChecks.Suspensions(locations));
        checks.AddRange(DoctorChecks.EplanInstallations(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        checks.AddRange(DoctorChecks.RunningEplan());
        var bridge = new BridgeClient(new PipeBridgeDiscovery(), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        await using (bridge.ConfigureAwait(false))
        {
            checks.AddRange(await DoctorChecks.BridgeAsync(bridge, cancellationToken).ConfigureAwait(false));
        }

        checks.Add(DoctorChecks.UserPathLength(DoctorChecks.ReadUserPathLength()));

        await output.WriteAsync(json ? FormatJson(HostInfo.Version, checks) + Environment.NewLine : Format(checks, culture)).ConfigureAwait(false);
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

    /// <summary>
    /// The JSON the Workbench reads (M5 design 4.4): the checks in output order and the worst status of all, where
    /// info counts as ok, so a healthy machine is ok although the version line is always info.
    /// </summary>
    internal static string FormatJson(string hostVersion, IReadOnlyList<DoctorCheck> checks) => new JsonObject
    {
        ["schemaVersion"] = SchemaVersion,
        ["hostVersion"] = hostVersion,
        ["status"] = Name(checks.Select(c => c.Status).Where(s => s != DoctorStatus.Info).DefaultIfEmpty(DoctorStatus.Ok).Max()),
        ["checks"] = new JsonArray([.. checks.Select(Json)]),
    }.ToJsonString(HostJson.Options);

    private static JsonObject Json(DoctorCheck check)
    {
        var json = new JsonObject { ["id"] = check.Id };
        if (check.Subject is { } subject)
        {
            json["subject"] = subject;
        }

        json["status"] = Name(check.Status);
        json["zh"] = check.Chinese;
        json["en"] = check.English;
        return json;
    }

    private static string Name(DoctorStatus status) => status switch
    {
        DoctorStatus.Ok => "ok",
        DoctorStatus.Info => "info",
        DoctorStatus.Warning => "warning",
        DoctorStatus.Failure => "failure",
        _ => throw new UnreachableException($"Doctor status {status} has no JSON name."),
    };
}
