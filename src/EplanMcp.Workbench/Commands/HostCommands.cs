using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Commands;

/// <summary>
/// The Host commands the Workbench uses (M5 design 4.4, 4.11), each through the one <see cref="IHostRunner"/>, with a
/// timeout (STS-04), its output read strictly as the documented JSON (VM client-config: schema 2; otherwise 1, ERR-08).
/// The Host writes; no page writes Host data itself.
/// </summary>
internal sealed class HostCommands(IHostRunner? runner)
{
    public const int SchemaVersion = 1;

    /// <summary>doctor asks every running EPLAN through its pipe, which may take a while.</summary>
    public static readonly TimeSpan DoctorTimeout = TimeSpan.FromSeconds(60);

    /// <summary>audit verify reads the whole audit log.</summary>
    public static readonly TimeSpan AuditTimeout = TimeSpan.FromSeconds(120);

    /// <summary>A dry run only reads the clients' files.</summary>
    public static readonly TimeSpan ClientConfigTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A write or a removal backs files up, writes them and records what it wrote; stopping it between the file and the
    /// record would leave them apart, so it gets much longer, and a timeout asks the engineer to look again.
    /// </summary>
    public static readonly TimeSpan ClientConfigWriteTimeout = TimeSpan.FromSeconds(180);

    /// <summary>How much of standard error the details keep: its last lines, each made plain.</summary>
    private const int ErrorLines = 20;

    private static readonly JsonSerializerOptions _strict = Strict();
    private static readonly Regex _secret = new(@"[A-Za-z0-9_-]{43,}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    // Decode JSON escapes before redaction; malformed output and stderr from secret-bearing commands are withheld.
    internal static string VmOutput(string output)
    {
        try
        { return _secret.Replace(JsonNode.Parse(output, documentOptions: new() { AllowDuplicateProperties = false })?.ToJsonString() ?? string.Empty, "<key>"); }
        catch (JsonException) { return string.Empty; }
    }

    public Task<VmConnectionReport> TestVmAsync(string url, string key, CancellationToken cancellationToken) =>
        RunAsync<VmConnectionReport>(["vm-test", "--url", url, "--key-stdin", "--format", "json"], ClientConfigTimeout, key, 1, cancellationToken);

    public async Task<ClientConfigReport> ConfigureVmClientsAsync(IReadOnlyList<string> clients, ClientConfigMode mode, string url, string key, CancellationToken cancellationToken)
    {
        string[] options = mode switch { ClientConfigMode.Remove => ["--remove"], ClientConfigMode.DryRun => ["--url", url, "--key-stdin", "--dry-run"], _ => ["--url", url, "--key-stdin"] };
        VmClientConfigReport report = await RunAsync<VmClientConfigReport>(["client-config", .. clients, "--vm", .. options, "--format", "json"],
            mode == ClientConfigMode.DryRun ? ClientConfigTimeout : ClientConfigWriteTimeout, mode == ClientConfigMode.Remove ? null : key, 2, cancellationToken).ConfigureAwait(false);
        return new(report.SchemaVersion, report.Mode, new(report.Server.Name, string.Empty, []), report.Problem, report.Clients);
    }

    /// <summary>Whether there is a Host to run; without one the pages that need it say so (NAV-05).</summary>
    public bool IsAvailable => runner is not null;

    public Task<DoctorReport> DoctorAsync(CancellationToken cancellationToken) =>
        RunAsync<DoctorReport>(["doctor", "--format", "json"], DoctorTimeout, cancellationToken);

    public Task<AddinVariants> VariantsAsync(CancellationToken cancellationToken) =>
        RunAsync<AddinVariants>(["addin-variants", "--format", "json"], ClientConfigTimeout, cancellationToken);

    public Task<AddinReport> RegisterAsync(IReadOnlyList<string> variants, bool uninstall, CancellationToken cancellationToken) =>
        RunAsync<AddinReport>([uninstall ? "uninstall-addin" : "install-addin", .. uninstall ? [] : variants.SelectMany(v => new[] { "--variant", v }), "--format", "json"], TimeSpan.FromMinutes(30), cancellationToken);

    public Task<ClientConfigReport> GeneratePluginAsync(bool preview, CancellationToken cancellationToken) =>
        RunAsync<ClientConfigReport>(["client-config", "claude-code", "--plugin", .. preview ? ["--dry-run"] : Array.Empty<string>(), "--format", "json"], ClientConfigWriteTimeout, cancellationToken);

    public Task<AuditVerifyReport> VerifyAuditAsync(CancellationToken cancellationToken) =>
        RunAsync<AuditVerifyReport>(["audit", "verify", "--format", "json"], AuditTimeout, cancellationToken);

    /// <summary>Writes, previews or removes the <c>eplan</c> entry of <paramref name="clients"/>; the Host backs files up.</summary>
    public Task<ClientConfigReport> ConfigureClientsAsync(IReadOnlyList<string> clients, ClientConfigMode mode, CancellationToken cancellationToken) =>
        RunAsync<ClientConfigReport>(
            ["client-config", .. clients, .. mode switch { ClientConfigMode.DryRun => ["--dry-run"], ClientConfigMode.Remove => ["--remove"], _ => Array.Empty<string>() }, "--format", "json"],
            mode == ClientConfigMode.DryRun ? ClientConfigTimeout : ClientConfigWriteTimeout,
            cancellationToken);

    /// <summary>
    /// <paramref name="output"/> as <typeparamref name="T"/>: one JSON object of the expected schema version, every field known,
    /// present and of its type, and no list with a null in it, which none of the documented ones has; else
    /// <see cref="HostCommandException"/> with <see cref="HostFailure.Unreadable"/>.
    /// </summary>
    internal static T Parse<T>(string output, int schemaVersion = SchemaVersion)
        where T : class
    {
        try
        {
            int? version;
            using (var document = JsonDocument.Parse(output))
            {
                version = document.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("schemaVersion", out JsonElement value)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
                if (HasNullInAList(document.RootElement))
                {
                    throw new HostCommandException(HostFailure.Unreadable, "A list in the output holds null.");
                }
            }

            if (version != schemaVersion)
            {
                throw new HostCommandException(HostFailure.Unreadable, version is null ? "The output has no schemaVersion." : string.Create(CultureInfo.InvariantCulture, $"schemaVersion {version} is not {schemaVersion}."));
            }

            return JsonSerializer.Deserialize<T>(output, _strict) ?? throw new HostCommandException(HostFailure.Unreadable, "The output is null.");
        }
        catch (JsonException ex)
        {
            throw new HostCommandException(HostFailure.Unreadable, ex.Message, ex);
        }
    }

    private Task<T> RunAsync<T>(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken) where T : class =>
        RunAsync<T>(arguments, timeout, null, 1, cancellationToken);

    private async Task<T> RunAsync<T>(string[] arguments, TimeSpan timeout, string? input, int schemaVersion, CancellationToken cancellationToken)
        where T : class
    {
        bool vm = input is not null || schemaVersion == 2;
        IHostRunner host = runner ?? throw new HostCommandException(HostFailure.Missing, "eplanmcp.exe is in neither layout beside the Workbench.");
        string commandLine = string.Join(' ', new[] { host.Executable }.Concat(arguments).Select(Quoted));
        HostRun run;
        try
        {
            run = await host.RunAsync(arguments, timeout, input, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (vm && ex is not OutOfMemoryException)
        {
            throw new HostCommandException(ex is HostCommandException failure ? failure.Failure : HostFailure.Unreadable, "VM command failed; secret diagnostics withheld.") { CommandLine = commandLine };
        }
        catch (HostCommandException ex)
        {
            throw new HostCommandException(ex.Failure, ex.Message, ex) { CommandLine = commandLine, Errors = Plain(ex.Errors) };
        }

        // 1 is a result too: a failed check, a broken chain, a client that could not be configured.
        if (run.ExitCode is not (0 or 1))
        {
            throw new HostCommandException(HostFailure.Failed, string.Create(CultureInfo.InvariantCulture, $"eplanmcp exited with {run.ExitCode}."))
            {
                CommandLine = commandLine,
                ExitCode = run.ExitCode,
                Errors = vm ? string.Empty : Plain(run.Errors),
            };
        }

        try
        {
            return Parse<T>(vm ? VmOutput(run.Output) : run.Output, schemaVersion);
        }
        catch (HostCommandException ex)
        {
            throw vm ? new HostCommandException(ex.Failure, "Invalid VM command output; check Host and Workbench versions.") { CommandLine = commandLine, ExitCode = run.ExitCode }
                : new HostCommandException(ex.Failure, ex.Message, ex) { CommandLine = commandLine, ExitCode = run.ExitCode, Errors = Plain(run.Errors) };
        }
    }

    private static bool HasNullInAList(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Null || HasNullInAList(item)),
        JsonValueKind.Object => element.EnumerateObject().Any(field => HasNullInAList(field.Value)),
        _ => false,
    };

    /// <summary>An argument as a command line would need it, for the details only: nothing here is ever run that way.</summary>
    private static string Quoted(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"') ? argument : '"' + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    /// <summary>The last lines of standard error, each made plain (CTL-12): it may quote a path or a project's text.</summary>
    private static string Plain(string errors) =>
        string.Join(Environment.NewLine, errors.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).TakeLast(ErrorLines).Select(line => ProjectText.Line(line)));

    private static JsonSerializerOptions Strict()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            Converters =
            {
                new StrictEnumConverter<CheckStatus>(),
                new StrictEnumConverter<AuditBreakProblem>(),
                new StrictEnumConverter<ClientConfigMode>(),
                new StrictEnumConverter<ClientAction>(),
                new StrictEnumConverter<ClientResultKind>(),
                new StrictEnumConverter<AddinAction>(),
                new StrictEnumConverter<AddinResultKind>(),
            },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
