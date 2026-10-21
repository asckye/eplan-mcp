using System.Text.Json;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// One JSON-RPC 2.0 message as <see cref="WorkbenchJson.Read"/> returns it: a request (<see cref="Id"/> and
/// <see cref="Method"/>), a notification (<see cref="Method"/> only) or a response (<see cref="Id"/> with either
/// <see cref="Result"/> or <see cref="Error"/>; an error to a request that could not be read has no ID). IDs are integers.
/// </summary>
public sealed record RpcMessage(string Jsonrpc, long? Id = null, string? Method = null, JsonElement? Params = null, JsonElement? Result = null, RpcError? Error = null);
