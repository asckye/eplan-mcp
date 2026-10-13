using System.Text.Encodings.Web;
using System.Text.Json;

namespace EplanMcp.Host;

/// <summary>
/// The Host's JSON settings for the pipe and for tool results: web defaults (camelCase), which the Contracts
/// round-trip tests pin against the Bridge's Newtonsoft settings. Chinese text stays readable in tool output.
/// </summary>
internal static class HostJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
