using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using Microsoft.VisualBasic.FileIO;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace EplanMcp.Host.Tools;

[McpServerToolType]
internal sealed class PartBuildTools
{
    public const string Tool = "eplan_part_build";
    [McpServerTool(Name = Tool, Title = "Build parts from an Excel or CSV list", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PartBuildResult))]
    [Description("Builds parts from up to 50 rows of an imports .xlsx/.csv into EPLAN's shared parts database. Omit/true previews (validates rows, lists field differences); false executes with engineer approval and a master-data backup (restore it to undo). Headers: eplan_tool_usage. Example: {\"file\":\"parts.csv\"}.")]
    public static async Task<CallToolResult> BuildAsync(IBridgeClient bridge, WritePipeline pipeline, ImportFiles files,
        [Description("Relative path below imports, .xlsx or UTF-8 .csv, at most 5 MB")] string file,
        [Description("Canonical field -> source header; omitted fields use their canonical header")] Dictionary<string, string>? mapping = null,
        [AllowedValues("append_new", "update_existing", "update_and_append")] string mode = "append_new",
        [AllowedValues("part_number")] string match_by = "part_number", [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        string? temp = null;
        var locks = new List<FileStream>();
        try
        {
            if (ImportFiles.Problem("file", file, [".csv", ".xlsx"]) is { } problem)
            { throw new InvalidDataException(problem); }
            if (mode is not ("append_new" or "update_existing" or "update_and_append") || match_by != "part_number")
            { throw new InvalidDataException("Use a supported mode and match_by part_number."); }
            ImportFile source = await files.ResolveAsync("file", file, 5 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            var input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            locks.Add(input);
            string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            input.Position = 0;
            if (hash != source.Sha256)
            { throw new InvalidDataException("The table changed while opening; call again."); }
            List<PartBuildTable.Row> table = PartBuildTable.Read(input, source.Path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            var result = new PartBuildResult { File = source.Path, Sha256 = hash, DryRun = dry_run };
            var values = new List<Dictionary<string, string>>();
            result.Compare(table, mapping, [], values);
            var search = new PartSearchArgs { BuildNumbers = [.. result.Rows.Select(r => r.PartNumber).Distinct(StringComparer.Ordinal)], Limit = 500 };
            PartSearchData before = await bridge.InvokeAsync<PartSearchArgs, PartSearchData>(OperationNames.PartSearch, search, cancellationToken).ConfigureAwait(false);
            if (before.Total != before.Items.Count)
            { throw new InvalidDataException("Too many variants; split the list."); }
            result.Rows.Clear();
            values.Clear();
            result.Compare(table, mapping, before.Items, values);
            result.Database = ResultShaper.CleanText(before.Database) ?? "";
            for (int i = 0; i < values.Count; i++)
            {
                foreach (string key in values[i].Keys.Where(k => k is "macro_file" or "picture_file"))
                {
                    try
                    {
                        if ((ImportFiles.Problem(key, values[i][key], key == "macro_file" ? [".ema", ".ems"] : [".png", ".jpg", ".jpeg", ".bmp"]) ?? (values[i][key].Contains("$(", StringComparison.Ordinal) ? "Path variables are forbidden." : null)) is { } invalid)
                        { throw new InvalidDataException(invalid); }
                        ImportFile reference = await files.ResolveAsync(key, values[i][key], PartMasterDataTools.MaxFileBytes, cancellationToken).ConfigureAwait(false);
                        locks.Add(new FileStream(reference.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
                        values[i][key] = reference.FullPath;
                        if (result.Rows[i].Diff.TryGetValue(key, out string[]? diff) && diff[0] == reference.FullPath)
                        { result.Rows[i].Diff.Remove(key); }
                    }
                    catch (Exception ex) when (ex is ImportFileException or InvalidDataException) { result.Rows[i].Errors.Add("Reference must be a permitted file below imports."); result.Rows[i].Action = "error"; }
                }
                PartBuildResult.Row row = result.Rows[i];
                if (row.Action == "update" && (mode == "append_new" || row.Diff.Count == 0) || row.Action == "create" && mode == "update_existing")
                { row.Action = "skip"; }
            }
            List<int> accepted = [.. Enumerable.Range(0, values.Count).Where(i => result.Rows[i].Action is "create" or "update")];
            if (!dry_run && result.Rows.All(r => r.Action != "error") && accepted.Count > 0)
            {
                if (!ResultShaper.Fits(result, ResultShaper.DefaultTokenBudget / 2))
                { throw new InvalidDataException("Preview exceeds the response budget; split the file."); }
                temp = Path.Combine(Path.GetFullPath(files.Root), "part-build-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                string xml = Path.Combine(temp, "parts.xml");
                foreach (int i in accepted)
                { foreach ((string key, string value) in before.Items.FirstOrDefault(p => p.PartNumber == result.Rows[i].PartNumber)?.BuildValues ?? []) { if (key is not ("macro_file" or "picture_file") && value.Length > 0) { values[i].TryAdd(key, value); } } }
                PartBuildResult.Xml(accepted.Select(i => values[i])).Save(xml);
                ImportFile generated = await files.ResolveAsync("file", Path.GetRelativePath(files.Root, xml), PartMasterDataTools.MaxFileBytes, cancellationToken).ConfigureAwait(false);
                var args = new PartImportArgs { File = generated.Path, FilePath = generated.FullPath, Sha256 = generated.Sha256, Mode = mode, MatchBy = match_by, BuildBaseline = accepted.ToDictionary(i => result.Rows[i].PartNumber, i => before.Items.FirstOrDefault(p => p.PartNumber == result.Rows[i].PartNumber)?.BuildValues, StringComparer.Ordinal) };
                WritePlan plan = await bridge.InvokeAsync<PartImportArgs, WritePlan>(OperationNames.PartImportPlan, args, cancellationToken).ConfigureAwait(false);
                if (plan.MasterData?.Database != before.Database)
                { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.PlanStale, Execution = ExecutionState.NotStarted, Message = "The configured database changed; call eplan_part_build again." }); }
                plan.Changes = [.. accepted.SelectMany(i => result.Rows[i].Diff.Select(d => new PlannedChange { Action = result.Rows[i].Action, Kind = "part", Name = result.Rows[i].PartNumber, Property = d.Key, OldValue = d.Value[0], NewValue = d.Value[1] }).DefaultIfEmpty(new PlannedChange { Action = "create", Kind = "part", Name = result.Rows[i].PartNumber }))];
                plan.Changes.Insert(0, new PlannedChange { Action = "read", Kind = "source", Name = source.Path, Property = "bytes / sha256", NewValue = FormattableString.Invariant($"{source.Bytes} / {hash}") });
                var request = new WriteRequest<PartImportArgs, PartImportArgs>(Tool, ToolTier.Destructive, OperationNames.PartImportPlan, OperationNames.PartImportExecute, args, p => new PartImportArgs { File = args.File, FilePath = args.FilePath, Sha256 = args.Sha256, Mode = mode, MatchBy = match_by, BuildBaseline = args.BuildBaseline, Fingerprint = p.Fingerprint }, false, PartMasterDataTools.Limit, "Building parts") { PreparedPlan = plan, AuditArguments = new { source.Path, source.Bytes, source.Sha256, mode, match_by, mapping, generatedHash = generated.Sha256 } };
                WriteOutcome<PartImportData> outcome = await pipeline.RunAsync<PartImportArgs, PartImportArgs, PartImportData>(request, progress, cancellationToken).ConfigureAwait(false);
                result.Execution = outcome.Execution;
                result.Import = JsonSerializer.Deserialize<PartImportResult>(PartMasterDataTools.Imported(outcome, source, mode, match_by, false).StructuredContent!.Value, HostJson.Options);
                try
                {
                    PartSearchData after = await bridge.InvokeAsync<PartSearchArgs, PartSearchData>(OperationNames.PartSearch, search, cancellationToken).ConfigureAwait(false);
                    result.ReadBack = [.. after.Items.Select(p => p.BuildValues.Append(new KeyValuePair<string, string>("part_number", p.PartNumber)).ToDictionary(p => p.Key, p => Display(p.Key, p.Value, files)))];
                    if (accepted.Any(i => !after.Items.Any(p => p.PartNumber == result.Rows[i].PartNumber && values[i].Where(v => v.Key != "part_number").All(v => PartBuildResult.Same(v.Key, p.BuildValues.GetValueOrDefault(v.Key, ""), v.Value)))))
                    { result.Hint = "Import completed; field read-back differs. Ask the engineer to check the parts and backup; do not repeat automatically."; }
                }
                catch (Exception ex) when (ex is BridgeCallException or OperationCanceledException) { result.Hint = "Import completed; field read-back failed. Ask the engineer to check EPLAN and backup."; }
            }
            if (dry_run && accepted.Count > 0 && result.Rows.All(r => r.Action != "error"))
            { result.Hint ??= WriteToolDefaults.ExecutionHint; }
            foreach (PartBuildResult.Row row in result.Rows)
            { row.PartNumber = ResultShaper.CleanText(row.PartNumber) ?? ""; foreach (string key in row.Diff.Keys) { row.Diff[key] = [.. row.Diff[key].Select(v => Display(key, v, files))]; } }
            result.UnknownColumns = [.. result.UnknownColumns.Select(s => ResultShaper.CleanText(s) ?? "")];
            if (!ResultShaper.Fits(result, ResultShaper.MaxTokenBudget))
            { if (result.Execution != ExecutionState.Committed) { throw new InvalidDataException("Response exceeds budget; split the file."); } result.Hint = "Read-back list shortened to fit the response; use eplan_part_search and check import.hint."; ResultShaper.FitItems(result, result.ReadBack, ResultShaper.MaxTokenBudget); }
            return ToolResults.Success(result.Execution == ExecutionState.Committed ? "Built parts after approval and backup; see rows, import and readBack." : "Validated all rows; nothing was written. Correct error rows before executing.", result);
        }
        catch (ImportFileException ex) { return ToolResults.Error(ex.Error); }
        catch (BridgeCallException ex) { ex.Error.Execution ??= ExecutionState.NotStarted; return ToolResults.Error(ex.Error); }
        catch (InvalidDataException ex) { var error = ToolResults.InvalidArgumentError([ex.Message]); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException or OverflowException or MalformedLineException)
        { var error = ToolResults.InvalidArgumentError(["Cannot build the table: use valid CSV/XLSX headers, mapping and values (see eplan_tool_usage)."]); error.Execution = ExecutionState.NotStarted; return ToolResults.Error(error); }
        finally { foreach (FileStream stream in locks) { await stream.DisposeAsync().ConfigureAwait(false); } if (temp is not null && string.Equals(Path.GetDirectoryName(temp), Path.TrimEndingDirectorySeparator(Path.GetFullPath(files.Root)), StringComparison.OrdinalIgnoreCase)) { try { Directory.Delete(temp, recursive: true); } catch (IOException) { /* Preserve the import outcome while EPLAN still holds the temporary XML. */ } } }
    }
    private static string Display(string key, string value, ImportFiles files) => ResultShaper.CleanText(key is "macro_file" or "picture_file" && Path.IsPathRooted(value) ? value.StartsWith(files.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(files.Root, value).Replace('\\', '/') : "[configured reference]" : value) ?? "";
}
