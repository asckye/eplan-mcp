using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Importing;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Project list imports through the Destructive pipeline (tool spec 5, SEC-31).</summary>
[McpServerToolType]
internal sealed class ListImportTools
{
    public const string ImportTool = "eplan_import_list";
    public const long MaxBytes = 100L * 1024 * 1024;

    [McpServerTool(Name = ImportTool, Title = "Import a project list", ReadOnly = false, Destructive = true, Idempotent = false,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ImportListResult))]
    [Description("Imports device or project parts lists from imports. Always needs approval and a project backup, in scratch too. Parts may take over a minute; undo is unverified. Example: {\"project\":\"Line1\",\"kind\":\"devices\",\"file\":\"incoming/devices.xml\",\"dry_run\":true}.")]
    public static async Task<CallToolResult> ImportListAsync(
        WritePipeline pipeline, ImportFiles files,
        [MinLength(1)] string project,
        [AllowedValues("devices", "parts")] string kind,
        [StringLength(260, MinimumLength = 1)] string file,
        [AllowedValues("xml", "csv")] string format = "xml",
        bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(project))
        {
            violations.Add("'project' is required: a name or path from eplan_project_list.");
        }

        if (kind is not ("devices" or "parts"))
        {
            violations.Add("'kind' must be devices or parts.");
        }

        if (format is not ("xml" or "csv") || (kind == "parts" && format != "xml"))
        {
            violations.Add("'format' must be xml or csv for devices, and xml for parts.");
        }

        if (ImportFiles.Problem("file", file, [format == "csv" ? ".csv" : ".xml"]) is { } problem)
        {
            violations.Add(problem);
        }

        if (violations.Count > 0)
        {
            return Invalid(violations);
        }

        try
        {
            ImportFile source = await files.ResolveAsync("file", file, MaxBytes, cancellationToken).ConfigureAwait(false);
            if (source.Bytes == 0)
            {
                return Invalid(["'file' must be nonempty; put a list in imports."]);
            }

            if (format == "xml")
            {
                var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using XmlReader reader = XmlReader.Create(source.FullPath, settings);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            var args = new ImportListArgs { Project = project, Kind = kind, Format = format, File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256 };
            var request = new WriteRequest<ImportListArgs, ImportListArgs>(ImportTool, ToolTier.Destructive,
                OperationNames.ImportListPlan, OperationNames.ImportListExecute, args,
                (plan, backup) => new ImportListArgs { Project = project, Kind = kind, Format = format, File = source.Path, FilePath = source.FullPath, Sha256 = source.Sha256, Fingerprint = plan.Fingerprint, Backup = backup },
                dry_run, TimeSpan.FromSeconds(ImportListArgs.ExecutionSeconds), "Importing project list");
            WriteOutcome<ImportListData> outcome = await pipeline.RunAsync<ImportListArgs, ImportListArgs, ImportListData>(request, progress, cancellationToken).ConfigureAwait(false);
            return Imported(outcome, source, kind, dry_run);
        }
        catch (ImportFileException ex)
        {
            return ToolResults.Error(ex.Error);
        }
        catch (XmlException)
        {
            return Invalid(["'file' must be valid XML without DTD or external entities; put the list in imports."]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Invalid(["'file' cannot be read; close it in other programs and put the list in imports."]);
        }
        catch (BridgeCallException ex)
        {
            if (ex.Error.Metadata?.GetValueOrDefault("eplanMessages") is { } json)
            {
                ex.Error.Metadata["eplanMessages"] = JsonSerializer.Serialize((JsonSerializer.Deserialize<List<SystemMessage>>(json, HostJson.Options) ?? []).Take(20).Select(CleanMessage), HostJson.Options);
            }

            return ToolResults.Error(ex.Error);
        }
    }

    private static CallToolResult Imported(WriteOutcome<ImportListData> outcome, ImportFile source, string kind, bool dryRun)
    {
        ImportListData? data = outcome.Data;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        var result = new ImportListResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dryRun,
            Kind = kind,
            File = ResultShaper.CleanText(source.Path),
            Bytes = source.Bytes,
            Sha256 = source.Sha256,
            Converter = data?.Converter ?? details.GetValueOrDefault("converter") ?? string.Empty,
            Before = data?.Before ?? JsonSerializer.Deserialize<ListImportCounts>(details["before"], HostJson.Options)!,
            After = data?.After,
            Backup = outcome.Backup,
            Recovered = outcome.Recovered,
            EplanMessages = [.. (data?.EplanMessages ?? []).Take(20).Select(CleanMessage)],
        };
        string hint = outcome.Recovered ? "The response was recovered; after counts and EPLAN messages are unavailable. Check the project in EPLAN. " : string.Empty;
        hint += ResultShaper.CleanText(data?.Warning) ?? "Added, updated and overwritten rows are known only after running; counts and entry part numbers do not verify every field. Undo after commit is unverified.";
        if (data is not null && data.Before.DeviceListEntries == data.After.DeviceListEntries && data.Before.ProjectParts == data.After.ProjectParts && data.Before.ArticleReferences == data.After.ArticleReferences)
        {
            hint += " No count changes were observed; this does not establish unchanged fields.";
        }

        int total = Math.Max(data?.EplanMessageCount ?? 0, data?.EplanMessages.Count ?? 0);
        result.Hint = hint + string.Create(CultureInfo.InvariantCulture, $" Listed up to 20 of {total} EPLAN messages.");
        ResultShaper.FitItems(result, result.EplanMessages);
        result.Truncated = result.EplanMessages.Count < total;
        result.Hint = result.Truncated ? hint + string.Create(CultureInfo.InvariantCulture, $" Listed {result.EplanMessages.Count} of {total} EPLAN messages.") : hint;
        return ToolResults.Success(outcome.Execution == ExecutionState.Committed
            ? "Imported a list into the project. A project backup was made first (see 'backup')."
            : "Dry run: would import a list into the project; row-level effects are known only after running. Nothing was changed.", result);
    }

    private static MessageListResult.MessageEntry CleanMessage(SystemMessage message) => new()
    {
        Level = ResultShaper.CleanText(message.Level),
        Number = ResultShaper.CleanText(message.Number),
        Text = ResultShaper.CleanText(message.Text),
        Occurrences = message.Occurrences,
    };

    private static CallToolResult Invalid(IReadOnlyList<string> violations) => ToolResults.Error(new BridgeError
    {
        Reason = ErrorReason.InvalidArgument,
        Execution = ExecutionState.NotStarted,
        Message = "Invalid arguments: " + string.Join(" ", violations),
        Metadata = new() { ["violations"] = string.Join("\n", violations) },
    });
}
