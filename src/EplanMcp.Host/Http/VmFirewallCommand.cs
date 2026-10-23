using System.Runtime.InteropServices;

namespace EplanMcp.Host.Http;

internal sealed class VmFirewallCommand(IVmFirewall firewall, Func<bool> elevated, Func<string> program)
{
    public static VmFirewallCommand ForMachine() => new(new VmFirewall(), VmFirewall.IsElevated,
        () => Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable."));

    public int Run(VmFirewallRequest request, TextWriter output)
    {
        try
        {
            if (request.Action != "show" && !elevated())
            {
                output.WriteLine("Administrator elevation is required for vm-firewall add/remove. Approve the Workbench UAC prompt or run eplanmcp.exe elevated.");
                return 1;
            }

            IReadOnlyList<VmFirewallRule> existing = firewall.Read();
            if (request.Action == "show")
            {
                output.WriteLine(existing.Count == 0 ? $"Firewall rule {VmFirewallRule.RuleName} is absent." : string.Join(Environment.NewLine, existing.Select(r => r.Describe())));
                return 0;
            }

            VmFirewallRule expected = request.Definition(program());
            if (existing.Count > 1 || existing.Any(r => !r.Matches(expected)))
            {
                output.WriteLine("The named rule differs from the requested rule. No changes made; use vm-firewall show to inspect it and supply its original port, host address and executable.");
                return 1;
            }

            if (request.Action == "add" && existing.Count == 0)
            {
                firewall.Add(expected);
            }
            else if (request.Action == "remove" && existing.Count == 1)
            {
                firewall.Remove(VmFirewallRule.RuleName);
            }

            output.WriteLine(request.Action == "add" ? "VM firewall rule present." : "VM firewall rule absent.");
            return 0;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            output.WriteLine($"VM firewall unavailable: {ex.Message}");
            return 1;
        }
    }
}
