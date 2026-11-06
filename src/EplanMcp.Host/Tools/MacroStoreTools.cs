using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed partial class MacroStoreTools
{
    internal const string Tool = "eplan_macro_store";

    [McpServerTool(Name = Tool, Title = "Store page objects as a macro", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    public static Task<CallToolResult> StoreAsync(IBridgeClient bridge, HostDataLocations locations, ILogger<MacroStoreTools> logger, RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) => RunAsync(bridge, locations,
            JsonSerializer.SerializeToNode(context.Params?.Arguments ?? new Dictionary<string, JsonElement>())!.AsObject(), cancellationToken, logger);

    internal static async Task<CallToolResult> RunAsync(IBridgeClient bridge, HostDataLocations locations, JsonObject input, CancellationToken cancellationToken, ILogger<MacroStoreTools>? logger = null)
    {
        ExportFolder? stagingFolder = null;
        try
        {
            if (input["name"] is not JsonValue nameValue || !nameValue.TryGetValue(out string? name) || string.IsNullOrEmpty(name)
                || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') || name.All(c => c == '.') || name.EndsWith('.') || ExportFileName.IsReserved(name))
            { return ToolResults.InvalidArguments(["'name' must be a file name using letters, digits, _, - and .; Windows device names are reserved."]); }
            if (input["operation"] is not JsonValue operationValue || !operationValue.TryGetValue(out string? operation) || operation is not ("window" or "page")
                || input["dry_run"] is { } dryValue && (dryValue is not JsonValue boolean || !boolean.TryGetValue<bool>(out _)))
            { return ToolResults.InvalidArguments(["Supply operation window or page; dry_run must be boolean."]); }
            bool dry = input["dry_run"]?.GetValue<bool>() ?? true;
            string extension = operation == "window" ? ".ema" : ".emp";
            string root = Path.GetFullPath(locations.Exports), directory = Path.Combine(root, "macros");
            string path = Path.Combine(directory, name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension);
            if (Directory.Exists(directory))
            { new ExportFolder(root, directory).CheckPlace(); }
            if (Path.Exists(path))
            { return ToolResults.InvalidArguments(["'name' already exists; choose a new macro name."]); }
            JsonObject args = input.DeepClone().AsObject();
            args["path"] = path;
            args["dry_run"] = true;
            JsonObject result = await bridge.InvokeAsync<JsonObject, JsonObject>(OperationNames.MacroStore, args, cancellationToken).ConfigureAwait(false);
            if (!dry)
            {
                stagingFolder = new ExportFolders(locations, TimeProvider.System).Create("macros");
                string staging = Path.Combine(stagingFolder.Path, Path.GetFileName(path));
                args["path"] = staging;
                args["fingerprint"] = result["fingerprint"]?.DeepClone();
                args["dry_run"] = false;
                result = await bridge.InvokeAsync<JsonObject, JsonObject>(OperationNames.MacroStore, args, PageWriteTools.DefaultLimit, null, cancellationToken).ConfigureAwait(false);
                if (stagingFolder.Files() is not { Count: 1 } files || files[0].Path != Path.GetFileName(path))
                { throw new IOException("The export did not produce exactly one macro file."); }
                using (var file = new FileStream(staging, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length == 0)
                    { throw new IOException("The macro file is empty."); }
                    result["size"] = file.Length;
                    result["sha256"] = Convert.ToHexStringLower(SHA256.HashData(file));
                }
                new ExportFolder(root, directory).CheckPlace();
                File.Move(staging, path, overwrite: false);
            }
            result["path"] = path;
            result["dryRun"] = dry;
            result["execution"] = dry ? ExecutionState.NotStarted : ExecutionState.Committed;
            if (dry)
            { result["hint"] = WriteToolDefaults.ExecutionHint; }
            return ToolResults.Success(dry ? "Macro export previewed." : "Macro file stored.", result);
        }
        catch (BridgeCallException ex) { return ToolResults.Error(ex.Error); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExportFolderException)
        { return ToolResults.Error(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "The macro file could not be stored or verified; choose a new name and check the Host export directory." }); }
        finally
        { if (stagingFolder is not null && !stagingFolder.TryDelete() && logger is not null) { LogCleanupFailed(logger); } }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the incomplete macro export folder")]
    private static partial void LogCleanupFailed(ILogger logger);

    internal static void Configure(IEnumerable<McpServerTool> tools)
    {
        Tool tool = tools.Single(t => t.ProtocolTool.Name == Tool).ProtocolTool;
        tool.Description = "Stores selected page objects as a window macro (.ema), or pages as a page macro (.emp), under the Host exports/macros directory. Use unchanged eplan_object_find locators. Omit dry_run to preview; false writes a new file. Existing names are refused. Project data and undo history stay unchanged. Read export tier; no approval. See eplan_tool_usage for examples.";
        tool.InputSchema = JsonDocument.Parse("""
            {"type":"object","additionalProperties":false,"required":["project","operation","name"],"properties":{"project":{"type":"string"},"operation":{"enum":["window","page"]},"name":{"type":"string"},"page":{"type":"string"},"locators":{"type":"array","minItems":1,"maxItems":500,"items":{"type":"object"}},"pages":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string"}},"representation":{"enum":["multi_line","MultiLine","Overview"],"default":"multi_line"},"variant":{"type":"integer","minimum":0,"maximum":15,"default":0},"reference_mm":{"type":"array","minItems":2,"maxItems":2,"items":{"type":"number"}},"description":{"oneOf":[{"type":"string"},{"type":"object","additionalProperties":{"type":"string"}}]},"dry_run":{"type":"boolean","default":true}}}
            """).RootElement.Clone();
        tool.OutputSchema = JsonDocument.Parse("""
            {"type":"object","required":["project","path","fingerprint","changes","execution","dryRun"],"properties":{"project":{"type":"string"},"path":{"type":"string"},"fingerprint":{"type":"string"},"changes":{"type":"array"},"size":{"type":"integer"},"sha256":{"type":"string"},"variants":{"type":"array","items":{"type":"integer"}},"execution":{"enum":["not_started","committed"]},"dryRun":{"type":"boolean"},"hint":{"type":"string"},"warning":{"type":"string"}}}
            """).RootElement.Clone();
    }
}
