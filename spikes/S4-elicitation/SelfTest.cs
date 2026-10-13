using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Spikes.S4;

/// <summary>
/// Drives this executable's <c>serve</c> mode with the SDK client, in both protocol eras, with scripted answers.
/// It proves the server half of S4 (and our reading of the SDK) before a real client is involved; it says
/// nothing about Claude Code itself.
/// </summary>
internal static class SelfTest
{
    private sealed record Case(string Name, string? ProtocolVersion, bool DeclareElicitation, string Form, int Rounds, string Action);

    private static readonly Case[] _cases =
    [
        new("mrtr-accept", null, true, "boolean", 1, "accept"),
        new("mrtr-accept-enum", null, true, "enum", 1, "accept"),
        new("mrtr-decline", null, true, "boolean", 1, "decline"),
        new("mrtr-cancel", null, true, "boolean", 1, "cancel"),
        new("mrtr-two-rounds", null, true, "boolean", 2, "accept"),
        new("mrtr-no-elicitation", null, false, "boolean", 1, "accept"),
        new("legacy-accept", "2025-11-25", true, "boolean", 1, "accept"),
        new("legacy-decline", "2025-11-25", true, "boolean", 1, "decline"),
        new("legacy-two-rounds", "2025-11-25", true, "boolean", 2, "accept"),
        new("legacy-no-elicitation", "2025-11-25", false, "boolean", 1, "accept"),
    ];

    public static async Task<int> RunAsync(string logDir)
    {
        var summary = new JsonArray();
        foreach (Case c in _cases)
        {
            summary.Add(await RunCaseAsync(c, logDir).ConfigureAwait(false));
        }

        Console.WriteLine(summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return 0;
    }

    private static async Task<JsonObject> RunCaseAsync(Case c, string logDir)
    {
        int handlerCalls = 0;
        var row = new JsonObject { ["case"] = c.Name };
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "s4-server",
                Command = Environment.ProcessPath!,
                Arguments = ["serve", "--log-dir", logDir],
            });
            var options = new McpClientOptions
            {
                ProtocolVersion = c.ProtocolVersion,
                Capabilities = c.DeclareElicitation
                    ? new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } }
                    : new ClientCapabilities(),
                Handlers = new McpClientHandlers { ElicitationHandler = c.DeclareElicitation ? AnswerScripted : null },
            };

            ValueTask<ElicitResult> AnswerScripted(ElicitRequestParams? request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref handlerCalls);
                return ValueTask.FromResult(Answer(c));
            }

            McpClient client = await McpClient.CreateAsync(transport, options).ConfigureAwait(false);
            CallToolResult result;
            await using (client.ConfigureAwait(false))
            {
                result = await client.CallToolAsync(
                    "s4_approve",
                    new Dictionary<string, object?> { ["form"] = c.Form, ["rounds"] = c.Rounds }).ConfigureAwait(false);
            }

            string text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "(no text)";
            JsonNode? report = TryParse(text);
            row["isError"] = result.IsError ?? false;
            row["negotiated"] = report?["negotiatedProtocolVersion"]?.DeepClone();
            row["outcome"] = report?["outcome"]?.DeepClone() ?? text;
            row["roundsAnswered"] = report?["roundsAnswered"]?.DeepClone();
            row["requestState"] = report?["requestState"]?["status"]?.DeepClone();
            row["lastAction"] = report?["lastAnswer"]?["action"]?.DeepClone();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            row["exception"] = ex.GetType().Name + ": " + ex.Message;
        }

        row["handlerCalls"] = handlerCalls;
        return row;
    }

    private static ElicitResult Answer(Case c) => c.Action switch
    {
        "accept" => new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement>
            {
                [c.Form == "enum" ? "decision" : "approve"] = c.Form == "enum"
                    ? JsonSerializer.SerializeToElement("approve")
                    : JsonSerializer.SerializeToElement(true),
            },
        },
        _ => new ElicitResult { Action = c.Action },
    };

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
