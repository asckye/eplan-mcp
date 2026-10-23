using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EplanMcp.Workbench.Vm;

internal static class VmNetwork
{
    public static bool IsAddress(string address) => IPAddress.TryParse(address, out IPAddress? ip)
        && ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() == address
        && ip.GetAddressBytes()[0] is > 0 and < 224 and not 127 && !ip.Equals(IPAddress.Broadcast);

    public static bool IsPort(string port) => int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is > 0 and <= 65535;

    public static IReadOnlyList<string> Addresses() => Select(NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address.ToString()));

    internal static IReadOnlyList<string> Select(IEnumerable<string> addresses)
    {
        string[] usable = [.. addresses.Where(IsAddress).Distinct().Order(StringComparer.Ordinal)];
        string[] preferred = [.. usable.Where(a => !a.StartsWith("169.254.", StringComparison.Ordinal))];
        return preferred.Length == 0 ? usable : preferred;
    }
}
