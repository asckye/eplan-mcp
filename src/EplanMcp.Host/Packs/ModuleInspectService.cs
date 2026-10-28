using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>M3 read seam only; no tool or operation registration and no project or macro writes.</summary>
internal sealed class ModuleInspectService(IBridgeClient bridge, ReferenceCopyService copies, string packRoot)
{
    public const string Operation = "module.inspect";

    public async Task<ModuleInspectResult> InspectAsync(string handle, IReadOnlyList<string> pages, PackResource macro, ModuleDefinition? module, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (macro.Kind != ResourceKind.Macro || macro.Storage != ResourceStorage.Bundled || PackPath.Problem(macro.Path) is not null
            || !macro.Path.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || !PlanPaths.IsHex(macro.Sha256, 64))
        { throw new PackFormatException("/macro: select a contained window macro resource with its exact SHA-256."); }
        string path = new PlanPaths(packRoot).Check(Path.Combine(packRoot, macro.Path.Replace('/', Path.DirectorySeparatorChar)));
        // Hold the original macro read-only throughout the Bridge call (A14); never stage it in the project.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (!Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).Equals(macro.Sha256, StringComparison.Ordinal))
        { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Message = "The module macro changed; verify the pack before inspecting it again." }); }
        var reader = new Reader(bridge, path, macro.Sha256, module);
        ReferenceExtractionResult result = await copies.ExtractAsync(handle, pages, reader, cancellationToken).ConfigureAwait(false);
        return new(reader.Inspection, result.Gaps, result.Problem);
    }

    private sealed class Reader(IBridgeClient bridge, string macro, string hash, ModuleDefinition? module) : IReferenceExtractor
    {
        public IReadOnlyList<string> RequiredOperations => [Operation];
        public ModuleInspection? Inspection { get; private set; }
        public async Task<ReferenceExtractionResult> ExtractAsync(string project, string resource, IReadOnlyList<string> pages, CancellationToken cancellationToken)
        {
            JsonElement data;
            try
            {
                data = await bridge.InvokeAsync<InspectRequest, JsonElement>(Operation, new(project, macro, hash, pages), cancellationToken).ConfigureAwait(false);
            }
            catch (BridgeCallException ex) when (ex.Error.Reason is ErrorReason.CapabilityMissing or ErrorReason.LicenceMissing)
            { return ReferenceExtractionResult.Missing(resource, ex.Error.Metadata?.GetValueOrDefault("missing") ?? (ex.Error.Reason == ErrorReason.LicenceMissing ? "licence:APIExtension" : "op:" + Operation), "Module inspection is unavailable; verify the Bridge operation and API Extension licence before retrying."); }
            ModuleInspection inspection = ModuleInspectionReader.Read(data, hash, module);
            if (data.GetProperty("apiExtensionLicence").ValueKind != JsonValueKind.True)
            { return ReferenceExtractionResult.Missing(resource, "licence:APIExtension", "Module inspection requires a confirmed API Extension licence at the time of reading; ask the engineer to check the licence."); }
            Inspection = inspection;
            return new([], inspection.Gaps);
        }
    }
    private sealed record InspectRequest(string Project, string Macro, string Sha256, IReadOnlyList<string> Pages);
}
