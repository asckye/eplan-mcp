namespace EplanMcp.Host.Bridge;

/// <summary>A Bridge found by discovery: its pipe name and the EPLAN process that owns it.</summary>
internal sealed record BridgeEndpoint(string PipeName, int ProcessId);
