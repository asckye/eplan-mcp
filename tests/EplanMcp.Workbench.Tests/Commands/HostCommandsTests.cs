using System.Text.Json.Nodes;
using EplanMcp.Workbench.Commands;

namespace EplanMcp.Workbench.Tests.Commands;

/// <summary>
/// The Host commands the Workbench runs (M5 design 4.4, 4.11): their argument lists and timeouts, every example of the
/// Host's own JSON snapshots read in full, and anything that is not that JSON taken as an error, never in part (ERR-08).
/// </summary>
[Trait("Size", "Small")]
public sealed class HostCommandsTests
{
    private readonly FakeHost _host = new();

    private HostCommands Commands => new(_host);

    public static TheoryData<string, string> AllExamples()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in new[] { "audit-verify-json", "client-config-json", "addin-json" })
        {
            foreach (string example in Snapshots.Examples(name))
            {
                data.Add(name, example);
            }
        }

        return data;
    }

    [Fact]
    public async Task Doctor_ReadsTheSnapshotWhole_EvenWhenAChecksFailed()
    {
        _host.Answer = _ => new HostRun(1, Snapshots.Read("doctor-json"));

        DoctorReport report = await Commands.DoctorAsync(TestContext.Current.CancellationToken);

        Run(["doctor", "--format", "json"], HostCommands.DoctorTimeout);
        Assert.Equal(("0.1.0", CheckStatus.Failure, 4), (report.HostVersion, report.Status, report.Checks.Count));
        Assert.Equal(new DoctorCheck("eplan.installation", CheckStatus.Ok, "EPLAN 2026.0.3.25421：E", "EPLAN 2026.0.3.25421: E", "2026.0.3.25421"), report.Checks[1]);
        Assert.Null(report.Checks[0].Subject);
    }

    [Theory]
    [MemberData(nameof(AllExamples))]
    public void EveryExampleOfTheHostsSnapshots_IsRead(string name, string example)
    {
        string json = Snapshots.Example(name, example);
        if (JsonNode.Parse(json)!["schemaVersion"]!.GetValue<int>() != HostCommands.SchemaVersion)
        {
            // client-config --vm reports (version 2) are for the host computer's wizard, which reads them itself.
            Assert.Equal(HostFailure.Unreadable, Assert.Throws<HostCommandException>(() => HostCommands.Parse<ClientConfigReport>(json)).Failure);
            return;
        }

        object report = name switch
        {
            "audit-verify-json" => HostCommands.Parse<AuditVerifyReport>(json),
            "addin-json" => HostCommands.Parse<AddinReport>(json),
            _ => HostCommands.Parse<ClientConfigReport>(json),
        };

        Assert.NotNull(report);
    }

    [Fact]
    public async Task AuditVerify_ReadsWhereTheChainBreaks()
    {
        _host.Answer = _ => new HostRun(1, Snapshots.Example("audit-verify-json", "notARecord"));

        AuditVerifyReport report = await Commands.VerifyAuditAsync(TestContext.Current.CancellationToken);

        Run(["audit", "verify", "--format", "json"], HostCommands.AuditTimeout);
        Assert.Equal((false, 2, new AuditBreak("2026-10-21.jsonl", 1, AuditBreakProblem.NotARecord)), (report.Intact, report.IntactRecords, report.Break));
    }

    [Theory]
    [InlineData(nameof(ClientConfigMode.DryRun), "--dry-run")]
    [InlineData(nameof(ClientConfigMode.Remove), "--remove")]
    [InlineData(nameof(ClientConfigMode.Write), null)]
    public async Task ClientConfig_NamesTheClientsAndTheMode(string mode, string? option)
    {
        _host.Answer = _ => new HostRun(0, Snapshots.Example("client-config-json", "dryRun"));

        await Commands.ConfigureClientsAsync(["codex", "cursor"], Enum.Parse<ClientConfigMode>(mode), TestContext.Current.CancellationToken);

        // A write or removal must not be stopped between the file and its record: it gets the long timeout.
        string[] expected = option is null ? ["client-config", "codex", "cursor", "--format", "json"] : ["client-config", "codex", "cursor", option, "--format", "json"];
        Run(expected, option == "--dry-run" ? HostCommands.ClientConfigTimeout : HostCommands.ClientConfigWriteTimeout);
    }

    [Fact]
    public async Task ClientConfig_ReadsEachClientsEntryNotesAndProblems()
    {
        _host.Answer = _ => new HostRun(1, Snapshots.Example("client-config-json", "write"));

        ClientConfigReport report = await Commands.ConfigureClientsAsync(["codex", "gemini-cli", "claude-desktop"], ClientConfigMode.Write, TestContext.Current.CancellationToken);

        Assert.Equal([ClientResultKind.Written, ClientResultKind.Unchanged, ClientResultKind.Failed], report.Clients.Select(c => c.Result));
        Assert.EndsWith(".bak_20261021T0815301234567Z", report.Clients[0].Backup, StringComparison.Ordinal);
        Assert.Equal("ambiguous_package", report.Clients[2].Problem!.Code);
        Assert.Equal(["serve"], report.Server.Args);
    }

    public static TheoryData<string> NotTheDocumentedJson => new()
    {
        "not json",
        "",
        "[]",
        """{"hostVersion":"0.1.0","status":"ok","checks":[]}""",
        """{"schemaVersion":2,"hostVersion":"0.1.0","status":"ok","checks":[]}""",
        """{"schemaVersion":"1","hostVersion":"0.1.0","status":"ok","checks":[]}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok","checks":[],"extra":true}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok"}""",
        """{"schemaVersion":1,"hostVersion":null,"status":"ok","checks":[]}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","status":"OK","checks":[]}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok","checks":[{"id":"x","status":"fine","zh":"","en":""}]}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","hostVersion":"0.2.0","status":"ok","checks":[]}""",
        """{"schemaVersion":1,"hostVersion":"0.1.0","status":"ok","checks":[null]}""",
    };

    public static TheoryData<string, string> NullsInClientConfigLists => new()
    {
        { "\"clients\":[", "\"clients\":[null," },
        { "\"notes\":[]", "\"notes\":[null]" },
        { "\"args\":[\"serve\"]", "\"args\":[null]" },
    };

    [Theory]
    [MemberData(nameof(NullsInClientConfigLists))]
    public void ANullInAnyList_MakesTheWholeOutputUnreadable(string field, string withNull)
    {
        string json = Snapshots.Example("client-config-json", "write");
        int at = json.IndexOf(field, StringComparison.Ordinal);
        Assert.True(at >= 0, field);

        var error = Assert.Throws<HostCommandException>(() => HostCommands.Parse<ClientConfigReport>(json[..at] + withNull + json[(at + field.Length)..]));

        Assert.Equal(HostFailure.Unreadable, error.Failure);
    }

    [Fact]
    public async Task TheDetails_QuoteAPathWithSpaces_AndKeepTheEndOfStandardErrorMadePlain()
    {
        _host.Executable = @"C:\Users\Jane Doe\EplanMcp\app\eplanmcp.exe";
        string errors = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line {i}")) + "\u202E\r\n";
        _host.Answer = _ => new HostRun(2, string.Empty, errors);

        var error = await Assert.ThrowsAsync<HostCommandException>(() => Commands.ConfigureClientsAsync(["code x"], ClientConfigMode.DryRun, TestContext.Current.CancellationToken));

        Assert.Equal(@"""C:\Users\Jane Doe\EplanMcp\app\eplanmcp.exe"" client-config ""code x"" --dry-run --format json", error.CommandLine);
        Assert.Equal(string.Join(Environment.NewLine, Enumerable.Range(11, 20).Select(i => $"line {i}")), error.Errors);
        string details = HostErrors.Details(error);
        Assert.StartsWith(error.CommandLine, details, StringComparison.Ordinal);
        Assert.EndsWith("line 30", details, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NotTheDocumentedJson))]
    public async Task AnythingButTheDocumentedJson_IsAnError_WithTheCommandLineAndExitCode(string output)
    {
        _host.Answer = _ => new HostRun(0, output);

        var error = await Assert.ThrowsAsync<HostCommandException>(() => Commands.DoctorAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFailure.Unreadable, error.Failure);
        Assert.Equal((_host.Executable + " doctor --format json", 0), (error.CommandLine, error.ExitCode));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task AnExitCodeOtherThanZeroOrOne_IsAnError_WhateverTheOutput(int exitCode)
    {
        _host.Answer = _ => new HostRun(exitCode, Snapshots.Read("doctor-json"));

        var error = await Assert.ThrowsAsync<HostCommandException>(() => Commands.DoctorAsync(TestContext.Current.CancellationToken));

        Assert.Equal((HostFailure.Failed, exitCode), (error.Failure, error.ExitCode));
    }

    [Fact]
    public async Task ATimeout_IsAnError_WithTheCommandLine()
    {
        _host.Answer = _ => throw new HostCommandException(HostFailure.TimedOut, "eplanmcp did not finish within 60 s.") { Errors = "reading audit" };

        var error = await Assert.ThrowsAsync<HostCommandException>(() => Commands.VerifyAuditAsync(TestContext.Current.CancellationToken));

        Assert.Equal((HostFailure.TimedOut, _host.Executable + " audit verify --format json", "reading audit"), (error.Failure, error.CommandLine, error.Errors));
    }

    [Fact]
    public async Task WithoutAHost_NothingRuns_AndTheErrorSaysItIsMissing()
    {
        var commands = new HostCommands(null);

        var error = await Assert.ThrowsAsync<HostCommandException>(() => commands.DoctorAsync(TestContext.Current.CancellationToken));

        Assert.False(commands.IsAvailable);
        Assert.Equal(HostFailure.Missing, error.Failure);
    }

    private void Run(string[] arguments, TimeSpan timeout)
    {
        (string[] ran, TimeSpan limit) = Assert.Single(_host.Runs);
        Assert.Equal(arguments, ran);
        Assert.Equal(timeout, limit);
    }

    [Fact]
    public void TheSnapshotsThemselves_AreSchemaVersion1()
    {
        Assert.Equal(HostCommands.SchemaVersion, JsonNode.Parse(Snapshots.Read("doctor-json"))!["schemaVersion"]!.GetValue<int>());
    }
}
