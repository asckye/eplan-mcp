using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Http;

internal sealed record VmTestRequest(string Url, bool Json)
{
    public static VmTestRequest? Parse(string[] options)
    {
        string? url = null;
        bool keyStdin = false, json = false;
        for (int i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--url" when url is null && i + 1 < options.Length:
                    url = options[++i];
                    break;
                case "--key-stdin" when !keyStdin:
                    keyStdin = true;
                    break;
                case "--format" when !json && i + 1 < options.Length && options[i + 1] == "json":
                    json = true;
                    i++;
                    break;
                default:
                    return null;
            }
        }

        return keyStdin && url is not null && RemoteServer.ValidUrl(url) ? new(url, json) : null;
    }
}
