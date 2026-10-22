using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Destructive PLC import with SEC-31 file checks and XML safety, backed by the Bridge's transaction guard.</summary>
[McpServerToolType]
internal sealed class PlcImportTools
{
    public const string Tool = "eplan_import_plc";
    public const string Pbf = "PlcDcXMLExchangerUniversal";
    public const string Aml = "PlcDcAMLExchangerGeneral";
    internal const string Incomplete = "File preflight is incomplete; new object names will be checked against pre-import full names inside the transaction; any match rolls the import back.";
    private static readonly string[] _errorFields = ["name", "text", "number"];

    [McpServerTool(Name = Tool, Title = "Import PLC bus data", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ImportPlcResult))]
    [Description("Imports PLC bus data from the imports folder into a project without those devices. Always needs approval and a project backup. File preflight is incomplete; newly imported names already in the project roll the import back. Undo after commit is unverified. Example: {\"project\":\"Line1\",\"file\":\"plc/Phoenix1.pbf\",\"converter\":\"PlcDcXMLExchangerUniversal\",\"dry_run\":true}.")]
    public static async Task<CallToolResult> ImportPlcAsync(WritePipeline pipeline, ImportFiles files,
        [Description("From eplan_project_list")] string project,
        [Description("Path below imports; at most 100 MB")] string file,
        [AllowedValues(Pbf, Aml)] string converter,
        bool dry_run = false, IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        string[] extensions = converter switch { Pbf => [".pbf"], Aml => [".aml"], _ => [".pbf", ".aml"] };
        if (converter is not (Pbf or Aml))
        {
            violations.Add("'converter' must be PlcDcXMLExchangerUniversal or PlcDcAMLExchangerGeneral.");
        }

        if (ImportFiles.Problem("file", file, extensions) is { } problem)
        {
            violations.Add(problem);
        }

        try
        {
            if (violations.Count > 0)
            {
                throw new BridgeCallException(ToolResults.InvalidArgumentError(violations));
            }

            ImportFile source = await files.ResolveAsync("file", file, PlcFilePreflight.MaxBytes, cancellationToken).ConfigureAwait(false);
            var stream = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (stream.ConfigureAwait(false))
            {
                await PlcFilePreflight.ValidateAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            var args = new ImportPlcArgs { Project = project, File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Converter = converter };
            var request = new WriteRequest<ImportPlcArgs, ImportPlcArgs>(Tool, ToolTier.Destructive, OperationNames.ImportPlcPlan, OperationNames.ImportPlcExecute, args,
                (plan, backup) => new ImportPlcArgs { Project = args.Project, File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, Converter = args.Converter, Fingerprint = plan.Fingerprint, Backup = backup },
                dry_run, TimeSpan.FromSeconds(300), "Importing PLC bus data");
            WriteOutcome<ImportPlcData> outcome = await pipeline.RunAsync<ImportPlcArgs, ImportPlcArgs, ImportPlcData>(request, progress, cancellationToken).ConfigureAwait(false);
            ImportPlcData? data = outcome.Data;
            var result = new ImportPlcResult
            {
                Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
                Execution = outcome.Execution,
                DryRun = dry_run,
                File = ResultShaper.CleanText(source.Path),
                Bytes = source.Bytes,
                Sha256 = source.Sha256,
                Converter = converter,
                Before = data?.Before ?? new ImportPlcCounts { Functions = Number(outcome.Plan, "functions"), PlcBoxes = Number(outcome.Plan, "plcBoxes"), ArticleReferences = Number(outcome.Plan, "articleReferences") },
                After = data?.After,
                AddedFunctions = data?.AddedFunctions,
                AddedPlcBoxes = data?.AddedPlcBoxes,
                NewObjectsSharingNames = data?.NewObjectsSharingNames,
                Backup = outcome.Backup,
                Recovered = outcome.Recovered,
                EplanMessages = [.. (data?.EplanMessages ?? []).Take(20).Select(m => new SystemMessage { Level = m.Level, Number = ResultShaper.CleanText(m.Number), Text = ResultShaper.CleanText(m.Text), Occurrences = m.Occurrences })],
                Truncated = data?.EplanMessageCount > 20,
                Hint = Incomplete + (outcome.Recovered ? " The response was lost; EPLAN reported a commit. Check the project before doing anything else." : string.Empty) + " " + ResultShaper.CleanText(data?.Warning),
            };
            int omitted = ResultShaper.FitItems(result, result.EplanMessages, ResultShaper.DefaultTokenBudget - 200);
            if (result.Truncated || omitted > 0)
            {
                result.Truncated = true;
                result.Hint += $" EPLAN reported {data?.EplanMessageCount.ToString(CultureInfo.InvariantCulture)} messages; some were omitted.";
            }

            return ToolResults.Success(dry_run ? "Dry run: file preflight is incomplete. New object names will be checked against pre-import full names inside the transaction; any match rolls the import back. Nothing was changed."
                : "Imported PLC bus data into the project. A project backup was made first (see 'backup').", result);
        }
        catch (ImportFileException ex)
        {
            return Error(ex.Error);
        }
        catch (BridgeCallException ex)
        {
            return Error(ex.Error);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return Error(ToolResults.InvalidArgumentError(["'file' must be readable, nonempty XML without DTDs or external XML references; correct the file in imports."]));
        }
    }

    private static int Number(WritePlan plan, string key) => int.Parse(plan.Details![key], CultureInfo.InvariantCulture);

    private static CallToolResult Error(BridgeError error)
    {
        error.Execution ??= ExecutionState.NotStarted;
        error.Message = ResultShaper.CleanText(error.Message);
        foreach (string key in error.Metadata?.Keys.Where(k => k is not ("duplicates" or "eplanMessages" or "backup")).ToArray() ?? [])
        {
            error.Metadata![key] = ResultShaper.CleanText(error.Metadata[key]);
        }

        foreach (string key in new[] { "duplicates", "eplanMessages" })
        {
            if (error.Metadata?.GetValueOrDefault(key) is not { } json || JsonNode.Parse(json) is not JsonArray list)
            {
                continue;
            }

            foreach (JsonObject row in list.OfType<JsonObject>())
            {
                foreach (string field in _errorFields.Where(f => row[f] is not null))
                {
                    row[field] = ResultShaper.CleanText(row[field]!.GetValue<string>());
                }
            }

            error.Metadata[key] = list.ToJsonString(HostJson.Options);
            while (list.Count > (key == "duplicates" ? 50 : 20) || (!ResultShaper.Fits(error, ResultShaper.DefaultTokenBudget - 200) && list.Count > 0))
            {
                list.RemoveAt(list.Count - 1);
                error.Metadata[key] = list.ToJsonString(HostJson.Options);
                error.Metadata["truncated"] = "true";
            }
        }

        return ToolResults.Error(error);
    }
}
