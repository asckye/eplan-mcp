using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using ModelContextProtocol;

namespace EplanMcp.Host.Packs;

/// <summary>
/// Installs someone else's <c>.eplanpack</c> (ADR-0024 sections 2 and 4): extracts and checks it completely in a new
/// folder of the data root, asks the engineer to confirm through the approval channels (EPLAN's dialog: Workbench
/// protocol version 1 has no field for it), installs exactly the checked folder, and audits the decision and the
/// outcome as pack_change. Nothing is left behind when it is refused. Stateless.
/// </summary>
internal sealed class PackInstaller(PackLibrary packs, ApprovalService approvals, IBridgeClient bridge)
{
    /// <summary>
    /// The installed pack, or the error; <paramref name="given"/> is the input as the tool got it, for the dialog and
    /// audit, and <paramref name="pack"/> the id the caller expects, which the file must hold.
    /// </summary>
    public async Task<(PackInstallResult? Installed, string? Digest, BridgeError? Error)> InstallAsync(
        string file, string given, string pack, string tool, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        string staging = Path.Combine(packs.Locations.Packs, $".confirm-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(packs.Locations.Packs);
            PackCheckResult check = await PackArchive.ExtractAsync(file, staging, PackLimits.Default, cancellationToken).ConfigureAwait(false);
            if (!check.Ok || check.Manifest is not { } manifest)
            {
                return (null, null, Invalid(check.Problems));
            }

            if (manifest.Id != pack)
            {
                return (null, null, ToolResults.InvalidArgumentError([$"The file holds the pack '{manifest.Id}', not '{pack}'; give 'pack' as '{manifest.Id}' to install it."]));
            }

            string digest = PackDigest.Compute(await File.ReadAllBytesAsync(Path.Combine(staging, PackManifest.FileName), cancellationToken).ConfigureAwait(false));
            if ((await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false)).Handshake?.Eplan is not { } eplan)
            {
                return (null, null, new BridgeError
                {
                    Reason = ErrorReason.ApprovalDenied,
                    Message = "Installing someone else's pack needs the engineer's confirmation in EPLAN's dialog, and no EPLAN is connected, so nothing was installed. Ask the engineer to start EPLAN, then call again.",
                });
            }

            string requestId = Guid.NewGuid().ToString("N");
            ApprovalResult decision = await approvals.ConfirmPackInstallAsync(requestId, tool, PackInstallInfo.From(given, manifest, digest, new FileInfo(file).Length), eplan, progress, cancellationToken).ConfigureAwait(false);
            var details = new JsonObject
            {
                ["action"] = "install",
                ["input"] = given,
                ["pack"] = manifest.Id,
                ["version"] = manifest.Version,
                ["digest"] = digest,
                ["channel"] = decision.Channel,
                ["decision"] = decision.Decision.ToString(),
            };
            if (decision.Decision != ApprovalDecision.Granted)
            {
                await packs.AuditAsync(requestId, tool, "failed", details, changed: false, cancellationToken).ConfigureAwait(false);
                return (null, null, approvals.Refusal(decision.Decision));
            }

            await packs.AuditAsync(requestId, tool, "started", details, changed: false, cancellationToken).ConfigureAwait(false);
            PackInstallResult installed = await packs.Installed.InstallAsync(staging, cancellationToken).ConfigureAwait(false);
            if (installed.Outcome == PackInstallOutcome.Refused)
            {
                await packs.AuditFailedAsync(requestId, tool, "refused", cancellationToken).ConfigureAwait(false);
                return (null, null, packs.Installed.InstalledDigest(manifest.Id, manifest.Version) is not null
                    ? new BridgeError { Reason = ErrorReason.AlreadyExists, Message = $"'{manifest.Id}' {manifest.Version} is installed already with other content, and versions never change; ask its author for a new version.", Metadata = PackLibrary.Problems(installed.Problems) }
                    : Invalid(installed.Problems));
            }

            await packs.AuditAsync(requestId, tool, "committed", new JsonObject { ["pack"] = manifest.Id, ["version"] = manifest.Version, ["digest"] = digest, ["outcome"] = installed.Outcome.ToString() }, changed: true, cancellationToken).ConfigureAwait(false);
            return (installed, digest, null);
        }
        catch (BridgeCallException ex)
        {
            return (null, null, ex.Error);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static BridgeError Invalid(IReadOnlyList<PackProblem> problems) => new()
    {
        Reason = ErrorReason.PackInvalid,
        Message = "The .eplanpack fails the checks of ADR-0024, so nothing was installed; metadata.problems lists why.",
        Metadata = PackLibrary.Problems(problems),
    };
}
