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

    /// <summary>Records adds and removes instead of refusing them; the real firewall is never touched either way.</summary>
    public bool Records { get; init; }

    public List<string> Changes { get; } = [];

    public void Add(VmFirewallRule rule) => Change("add " + rule.RemoteAddress);

    public void Remove(string name) => Change("remove " + name);

    private void Change(string change) => (Records ? (Action)(() => Changes.Add(change)) : () => throw new InvalidOperationException("Tests must never mutate the firewall."))();
}
