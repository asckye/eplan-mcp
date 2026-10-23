using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.Http;

internal sealed class FakeVmFirewall : IVmFirewall
{
    public IReadOnlyList<VmFirewallRule> Rules { get; init; } = [];
    public Exception? Problem { get; init; }
    public int Reads { get; private set; }

    public IReadOnlyList<VmFirewallRule> Read()
    {
        Reads++;
        if (Problem is not null)
        {
            throw Problem;
        }

        return Rules;
    }

    public void Add(VmFirewallRule rule) => throw new InvalidOperationException("Tests must never mutate the firewall.");
    public void Remove(string name) => throw new InvalidOperationException("Tests must never mutate the firewall.");
}
