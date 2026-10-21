using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Commands;

/// <summary>
/// The Host commands the Workbench uses (M5 design 4.4, 4.11), each through the one <see cref="IHostRunner"/>, with a
/// timeout (STS-04), its output read strictly as the documented JSON of schema version 1 and nothing else (ERR-08).
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

    /// <summary>Whether there is a Host to run; without one the pages that need it say so (NAV-05).</summary>
    public bool IsAvailable => runner is not null;

    public Task<DoctorReport> DoctorAsync(CancellationToken cancellationToken) =>
        RunAsync<DoctorReport>(["doctor", "--format", "json"], DoctorTimeout, cancellationToken);

    public Task<AuditVerifyReport> VerifyAuditAsync(CancellationToken cancellationToken) =>
        RunAsync<AuditVerifyReport>(["audit", "verify", "--format", "json"], AuditTimeout, cancellationToken);

    /// <summary>Writes, previews or removes the <c>eplan</c> entry of <paramref name="clients"/>; the Host backs files up.</summary>
    public Task<ClientConfigReport> ConfigureClientsAsync(IReadOnlyList<string> clients, ClientConfigMode mode, CancellationToken cancellationToken) =>
        RunAsync<ClientConfigReport>(
            ["client-config", .. clients, .. mode switch { ClientConfigMode.DryRun => ["--dry-run"], ClientConfigMode.Remove => ["--remove"], _ => Array.Empty<string>() }, "--format", "json"],
            mode == ClientConfigMode.DryRun ? ClientConfigTimeout : ClientConfigWriteTimeout,
            cancellationToken);

    /// <summary>
    /// <paramref name="output"/> as <typeparamref name="T"/>: one JSON object of schema version 1, every field known,
    /// present and of its type, and no list with a null in it, which none of the documented ones has; else
    /// <see cref="HostCommandException"/> with <see cref="HostFailure.Unreadable"/>.
    /// </summary>
    internal static T Parse<T>(string output)
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

            if (version != SchemaVersion)
            {
                throw new HostCommandException(HostFailure.Unreadable, version is null ? "The output has no schemaVersion." : string.Create(CultureInfo.InvariantCulture, $"schemaVersion {version} is not {SchemaVersion}."));
            }

            return JsonSerializer.Deserialize<T>(output, _strict) ?? throw new HostCommandException(HostFailure.Unreadable, "The output is null.");
        }
        catch (JsonException ex)
        {
            throw new HostCommandException(HostFailure.Unreadable, ex.Message, ex);
        }
    }

    private async Task<T> RunAsync<T>(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        IHostRunner host = runner ?? throw new HostCommandException(HostFailure.Missing, "eplanmcp.exe is in neither layout beside the Workbench.");
        string commandLine = string.Join(' ', new[] { host.Executable }.Concat(arguments).Select(Quoted));
        HostRun run;
        try
        {
            run = await host.RunAsync(arguments, timeout, cancellationToken).ConfigureAwait(false);
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
                Errors = Plain(run.Errors),
            };
        }

        try
        {
            return Parse<T>(run.Output);
        }
        catch (HostCommandException ex)
        {
            throw new HostCommandException(ex.Failure, ex.Message, ex) { CommandLine = commandLine, ExitCode = run.ExitCode, Errors = Plain(run.Errors) };
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
            },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
