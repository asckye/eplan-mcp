namespace EplanMcp.Host;

internal enum CliCommand
{
    Serve,
    Doctor,
    AuditVerify,
    Version,
}

/// <summary>
/// The command line of <c>eplanmcp</c>. Only doctor and audit verify take <c>--format json</c>, the output the
/// Workbench reads (M5 design 4.4, 4.11); any other option or format is a wrong command line.
/// </summary>
internal sealed record CliArguments(CliCommand Command, bool Json)
{
    public const string Usage = "usage: eplanmcp [serve] | doctor [--format json] | audit verify [--format json] | --version";

    /// <summary>The command, or null for a wrong command line.</summary>
    public static CliArguments? Parse(string[] args) => args switch
    {
        [] or ["serve"] => new(CliCommand.Serve, Json: false),
        ["--version"] => new(CliCommand.Version, Json: false),
        ["doctor", .. var options] => WithFormat(CliCommand.Doctor, options),
        ["audit", "verify", .. var options] => WithFormat(CliCommand.AuditVerify, options),
        _ => null,
    };

    private static CliArguments? WithFormat(CliCommand command, string[] options) => options switch
    {
        [] => new(command, Json: false),
        ["--format", "json"] => new(command, Json: true),
        _ => null,
    };
}
