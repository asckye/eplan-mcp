using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using ModelContextProtocol;

namespace EplanMcp.Host.Packs;

internal sealed class PluginEnableService(PackLibrary packs, PluginStateStore store, ApprovalService approvals, IBridgeClient bridge)
{
    public async Task<PluginEnableResult> SetAsync(string plugin, string version, bool enabled,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        var (state, revision, warnings) = await PluginStateQuery.ReadAsync(store, cancellationToken).ConfigureAwait(false);
        if (state is null)
        { return new(plugin, version, null, false, warnings); }
        var identity = new PluginIdentity(plugin, version);
        bool wasEnabled = state.Enabled.Contains(identity);
        if (enabled)
        {
            string folder = packs.Installed.FolderOf(plugin, version);
            if (!Directory.Exists(folder))
            {
                if (wasEnabled)
                { return new(plugin, version, true, false, ["The enabled plugin version is missing; restore its installed files."]); }
                throw new BridgeCallException(PackLibrary.NotFound(plugin, version));
            }
            var (manifest, _, problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
            if (manifest is null || problems.Count > 0 || manifest.Id != plugin || manifest.Version != version)
            { return new(plugin, version, wasEnabled, false, ["The exact installed manifest is unreadable; restore the installed plugin and try again."]); }
        }
        if (wasEnabled == enabled)
        { return new(plugin, version, wasEnabled, false, []); }
        List<PluginIdentity> next = [.. state.Enabled];
        int index = next.FindIndex(p => p.Id == plugin);
        if (!enabled)
        { next.RemoveAt(index); }
        else if (index < 0)
        { next.Add(identity); }
        else
        { next[index] = identity; }
        string requestId = Guid.NewGuid().ToString("N"), action = enabled ? "enable" : "disable";
        string fingerprint = PlanStore.Hash(PackJson.Serialize(state with { Enabled = next }));
        var plan = new WritePlan
        {
            Fingerprint = fingerprint,
            Changes = [new PlannedChange { Action = action, Kind = "plugin", Name = $"{plugin}@{version}", Property = "global" }],
        };
        var details = new JsonObject { ["action"] = action, ["plugin"] = plugin, ["version"] = version, ["scope"] = "global" };
        bool changed = false;
        await packs.AuditAsync(requestId, PluginEnableTools.Tool, "started", details, false, cancellationToken).ConfigureAwait(false);
        try
        {
            EplanInstanceInfo? eplan = (await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake?.Eplan;
            if (eplan is null)
            { throw new BridgeCallException(approvals.Refusal(ApprovalDecision.Unavailable)); }
            ApprovalResult decision = await approvals.ConfirmPluginEnableAsync(requestId, plan, eplan, progress, cancellationToken).ConfigureAwait(false);
            decision.Outcome?.Dispose();
            details["channel"] = decision.Channel;
            details["decision"] = decision.Decision.ToString();
            if (decision.Decision != ApprovalDecision.Granted)
            { throw new BridgeCallException(approvals.Refusal(decision.Decision)); }
            try
            { await store.WriteAsync(state with { Enabled = next }, revision, cancellationToken).ConfigureAwait(false); }
            catch (PackFormatException)
            { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Retryable = true, Message = "Plugin state changed or is busy; read eplan_pack_list and retry the enable request." }); }
            changed = true;
            await packs.AuditAsync(requestId, PluginEnableTools.Tool, "committed", details, true, CancellationToken.None).ConfigureAwait(false);
            return new(plugin, version, enabled, true, []);
        }
        catch (Exception ex) when (ex is BridgeCallException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (!changed)
            { await packs.AuditAsync(requestId, PluginEnableTools.Tool, "failed", details, false, CancellationToken.None).ConfigureAwait(false); }
            throw;
        }
    }
}
