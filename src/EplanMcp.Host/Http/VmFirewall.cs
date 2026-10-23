using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace EplanMcp.Host.Http;

/// <summary>COM objects are local to each call; reading never invokes a policy setter (SEC-29).</summary>
internal sealed class VmFirewall : IVmFirewall
{
    private static dynamic Create(string id) => Activator.CreateInstance(Type.GetTypeFromProgID(id, throwOnError: true)!)!;

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IReadOnlyList<VmFirewallRule> Read()
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        dynamic rules = policy.Rules;
        var found = new List<VmFirewallRule>();
        try
        {
            foreach (dynamic rule in (IEnumerable)rules)
            {
                try
                {
                    if (string.Equals((string)rule.Name, VmFirewallRule.RuleName, StringComparison.Ordinal))
                    {
                        found.Add(new((string)rule.Name, (int)rule.Protocol, (string)rule.LocalPorts, (string)rule.ApplicationName,
                            (string)rule.RemoteAddresses, (int)rule.Profiles, (int)rule.Direction, (int)rule.Action, (bool)rule.Enabled, (bool)rule.EdgeTraversal));
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(rule);
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(rules);
            Marshal.FinalReleaseComObject(policy);
        }

        return found;
    }

    public void Add(VmFirewallRule definition)
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        dynamic rules = policy.Rules;
        dynamic rule = Create("HNetCfg.FWRule");
        try
        {
            rule.Name = definition.Name;
            rule.Protocol = definition.Protocol; // Protocol must precede ports in the COM API.
            rule.LocalPorts = definition.Port;
            rule.ApplicationName = definition.Program;
            rule.RemoteAddresses = definition.RemoteAddress;
            rule.Profiles = definition.Profiles;
            rule.Direction = definition.Direction;
            rule.Action = definition.Action;
            rule.Enabled = definition.Enabled;
            rule.EdgeTraversal = definition.EdgeTraversal;
            rules.Add(rule);
        }
        finally
        {
            Marshal.FinalReleaseComObject(rule);
            Marshal.FinalReleaseComObject(rules);
            Marshal.FinalReleaseComObject(policy);
        }
    }

    public void Remove(string name)
    {
        dynamic policy = Create("HNetCfg.FwPolicy2");
        dynamic rules = policy.Rules;
        try
        {
            rules.Remove(name);
        }
        finally
        {
            Marshal.FinalReleaseComObject(rules);
            Marshal.FinalReleaseComObject(policy);
        }
    }
}
