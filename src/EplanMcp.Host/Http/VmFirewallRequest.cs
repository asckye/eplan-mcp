using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace EplanMcp.Host.Http;

internal sealed record VmFirewallRequest(string Action, int Port, string HostAddress, bool Json = false)
{
    public static VmFirewallRequest? Parse(string action, string[] options)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < options.Length; i++)
        {
            string option = options[i];
            if (option is not ("--port" or "--host-address" or "--format") || ++i >= options.Length || !values.TryAdd(option, options[i]))
            {
                return null;
            }
        }

        return action is "show" or "add" or "remove" && values.TryGetValue("--port", out string? number)
            && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
            && values.TryGetValue("--host-address", out string? address) && IsHostAddress(address)
            && (!values.TryGetValue("--format", out string? format) || (action == "show" && format == "json"))
            ? new(action, port, address, values.ContainsKey("--format")) : null;
    }

    private static bool IsHostAddress(string address) => IPAddress.TryParse(address, out IPAddress? ip)
        && ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() == address
        && ip.GetAddressBytes()[0] is > 0 and < 224 && !ip.Equals(IPAddress.Broadcast);

    public VmFirewallRule Definition(string program)
    {
        if (!Path.IsPathFullyQualified(program) || !string.Equals(Path.GetFileName(program), "eplanmcp.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Run the eplanmcp.exe apphost to configure its firewall rule.");
        }

        return new(VmFirewallRule.RuleName, 6, Port.ToString(CultureInfo.InvariantCulture), program, HostAddress, 3, 1, 1, true, false);
    }
}

internal sealed record VmFirewallRule(string Name, int Protocol, string Port, string Program, string RemoteAddress,
    int Profiles, int Direction, int Action, bool Enabled, bool EdgeTraversal)
{
    public const string RuleName = "EplanMcp.Vm.Http";

    public string Describe() => FormattableString.Invariant($"{Name}: remote={RemoteAddress}; port={Port}; program={Program}; profiles={ProfileNames()} ({Profiles}); protocol={Protocol}; direction={Direction}; action={Action}; enabled={Enabled}; edgeTraversal={EdgeTraversal}");

    private string ProfileNames() => string.Join(",", new[] { (1, "Domain"), (2, "Private"), (4, "Public") }.Where(p => (Profiles & p.Item1) != 0).Select(p => p.Item2));

    public bool Matches(VmFirewallRule expected) => this with
    {
        Program = expected.Program,
        RemoteAddress = expected.RemoteAddress,
    } == expected && string.Equals(Program, expected.Program, StringComparison.OrdinalIgnoreCase)
        && (RemoteAddress == expected.RemoteAddress || RemoteAddress == expected.RemoteAddress + "/32" || RemoteAddress == expected.RemoteAddress + "/255.255.255.255");
}

internal interface IVmFirewallReader
{
    public IReadOnlyList<VmFirewallRule> Read();
}

internal interface IVmFirewall : IVmFirewallReader
{
    public void Add(VmFirewallRule rule);
    public void Remove(string name);
}
