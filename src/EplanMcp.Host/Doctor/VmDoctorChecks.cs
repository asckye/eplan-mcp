using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Doctor;

internal static class VmDoctorChecks
{
    public static IReadOnlyList<DoctorCheck> Read(Func<IEnumerable<HttpServiceState>> states, Func<int, DateTimeOffset?> startOf, IVmFirewallReader firewall)
    {
        var checks = new List<DoctorCheck>();
        try
        {
            HttpServiceState[] running = [.. states().Where(s => startOf(s.ProcessId) == s.ProcessStart)];
            if (running.Length == 0)
            {
                checks.Add(new(DoctorCheckIds.HttpService, DoctorStatus.Info, "此数据目录的 HTTP 服务未运行", "HTTP service for this data root is not running"));
            }
            foreach (HttpServiceState state in running)
            {
                string endpoint = FormattableString.Invariant($"{state.Address}:{state.Port} (PID {state.ProcessId})");
                checks.Add(new(DoctorCheckIds.HttpService, DoctorStatus.Ok, $"此数据目录的 HTTP 服务正在运行：{endpoint}", $"HTTP service for this data root is running: {endpoint}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            checks.Add(new(DoctorCheckIds.HttpService, DoctorStatus.Warning, "HTTP 服务状态无法读取或验证", "HTTP service state cannot be read or verified"));
        }

        try
        {
            IReadOnlyList<VmFirewallRule> rules = firewall.Read();
            checks.Add(rules.Count == 0
                ? new(DoctorCheckIds.VmFirewall, DoctorStatus.Info, $"防火墙规则 {VmFirewallRule.RuleName} 不存在", $"Firewall rule {VmFirewallRule.RuleName} is absent")
                : new(DoctorCheckIds.VmFirewall, DoctorStatus.Info, "防火墙规则：" + string.Join("; ", rules.Select(r => r.Describe())), "Firewall rule: " + string.Join("; ", rules.Select(r => r.Describe()))));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            checks.Add(new(DoctorCheckIds.VmFirewall, DoctorStatus.Warning, "无法只读检查防火墙规则", "Firewall rule could not be inspected read-only"));
        }

        return checks;
    }
}
