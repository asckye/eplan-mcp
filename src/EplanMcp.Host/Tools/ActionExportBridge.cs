using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;

namespace EplanMcp.Host.Tools;

/// <summary>One call's typed adaptation to ExportRunner's existing contract; no shared export hooks or state.</summary>
internal sealed class ActionExportBridge(IBridgeClient bridge, ActionRunArgs args, HostDataLocations locations) : IBridgeClient
{
    public WritePlan? Plan { get; private set; }
    public ActionRunData? Data { get; private set; }
    public JsonArray Files { get; } = [];
    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs value, CancellationToken cancellationToken) => InvokeAsync<TArgs, TData>(operation, value, TimeSpan.FromSeconds(30), null, cancellationToken);
    public async Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs value, TimeSpan timeout, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        if (operation == OperationNames.ActionRunPlan)
        {
            Plan = await bridge.InvokeAsync<ActionRunArgs, WritePlan>(operation, args, cancellationToken).ConfigureAwait(false);
            return (TData)(object)new ExportPlanData { Project = Plan.Project };
        }
        args.Directory = ((ExportArgs)(object)value!).Directory;
        Data = await bridge.InvokeAsync<ActionRunArgs, ActionRunData>(operation, args, timeout, progress, cancellationToken).ConfigureAwait(false);
        try
        {
            // Validate before opening any file; an exception here lets ExportRunner remove its own folder.
            IReadOnlyList<ExportFile> files = new ExportFolder(locations.Exports, args.Directory!).Files();
            foreach (ExportFile file in files)
            {
                if (file.Bytes <= 0) { throw Failed(); }
                using FileStream stream = File.OpenRead(Path.Combine(args.Directory!, file.Path));
                if (args.Entry == "backup/MASTERDATA")
                {
                    byte[] header = new byte[6];
                    if (stream.Read(header) != 6 || !header.AsSpan().SequenceEqual(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })) { throw Failed(); }
                    stream.Position = 0;
                }
                Files.Add(new JsonObject { ["path"] = file.Path, ["bytes"] = file.Bytes, ["sha256"] = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) });
            }
            return (TData)(object)new ExportRunData { Project = Plan!.Project };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExportFolderException) { throw Failed(); }
    }
    private static BridgeCallException Failed() => new(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "The action left an empty file or an invalid EPLAN archive; no files were kept." });
    public Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken) => bridge.GetStatusAsync(cancellationToken);
    public Task<HandshakeResult> SelectAsync(int processId, CancellationToken cancellationToken) => bridge.SelectAsync(processId, cancellationToken);
    public Task<WriteResult<TData>> InvokeWriteAsync<TArgs, TData>(string operation, TArgs value, string requestId, TimeSpan limit, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken) => bridge.InvokeWriteAsync<TArgs, TData>(operation, value, requestId, limit, progress, cancellationToken);
    public Task<OutcomeGetData?> FetchOutcomeAsync(int processId, string requestId, CancellationToken cancellationToken) => bridge.FetchOutcomeAsync(processId, requestId, cancellationToken);
}
