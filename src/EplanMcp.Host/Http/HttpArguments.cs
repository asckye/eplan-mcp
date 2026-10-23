using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace EplanMcp.Host.Http;

internal sealed record HttpArguments(IPAddress Address, int Port, int? ProcessId)
{
    public static HttpArguments? Parse(string[] options)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < options.Length; i++)
        {
            string option = options[i];
            if (option == "--http" ? !values.TryAdd(option, string.Empty)
                : option is not ("--address" or "--port" or "--process") || ++i >= options.Length || !values.TryAdd(option, options[i]))
            {
                return null;
            }
        }

        return values.ContainsKey("--http") && values.TryGetValue("--address", out string? address)
            && IPAddress.TryParse(address, out IPAddress? ip) && ip.ToString() == address && ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Any)
            && int.TryParse(values.GetValueOrDefault("--port", "8766"), NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
            && int.TryParse(values.GetValueOrDefault("--process", "0"), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            && (!values.ContainsKey("--process") || pid > 0)
            ? new(ip, port, pid == 0 ? null : pid) : null;
    }
}
