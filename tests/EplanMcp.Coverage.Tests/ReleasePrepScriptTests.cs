using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Coverage.Tests;

[Trait("Size", "Medium")]
public sealed class ReleasePrepScriptTests
{
    private const string Changelog = "# Changelog\n\n## [Unreleased]\n\n### Added\n- 新功能\n\n```md\n## [Unreleased]\n```\n\n## [0.4.0] - 2026-10-27\n\n- 旧记录\n";
    private const string Ledger = """
        # Large 待验台账
        | 变更集 / 定位 | 受影响 Large 类 | 状态与剩余条件 / 证据 |
        |---|---|---|
        | C1 | `ApiTryTests` | 待运行：撤销 |
        | PDF | `ImportPdfCommentsTests` | 受阻：缺夹具 |
        | Bug | `FailedTests` | 失败待修：读回 |
        | Partial | `PartialTests` | 待运行：部分通过，剩余撤销 |
        | Done | `DoneTests` | 已通过：2026-11-01；证据中提到待运行历史 |
        | Unknown | `NewTests` | 待补证据：x \| y |
        """;

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Changelog_ExtractsOnlyUnreleased_PreservesHistoryAndFences(string newline)
    {
        using JsonDocument result = await RunAsync($"Get-ReleaseChangelog {Ps(Changelog.Replace("\n", newline, StringComparison.Ordinal))} '0.4.1' '2026-11-06' | ConvertTo-Json -Compress");
        string text = result.RootElement.GetProperty("Changelog").GetString()!;
        string section = result.RootElement.GetProperty("Section").GetString()!;
        Assert.StartsWith("# Changelog\n\n## [Unreleased]\n\n## [0.4.1] - 2026-11-06\n", text, StringComparison.Ordinal);
        Assert.Contains("```md\n## [Unreleased]\n```", section, StringComparison.Ordinal);
        Assert.DoesNotContain("旧记录", section, StringComparison.Ordinal);
        Assert.EndsWith("## [0.4.0] - 2026-10-27\n\n- 旧记录\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    [Theory]
    [InlineData("# Changelog\n- missing")]
    [InlineData("## [Unreleased]\n\n### Added\n")]
    [InlineData("## [Unreleased]\n## [Unreleased]\n- duplicated")]
    [InlineData("## [Unreleased]\n- new\n## [0.4.1] - 2026-10-01\n- duplicate version")]
    public async Task Changelog_MissingEmptyOrDuplicateSection_Rejects(string text)
    {
        using JsonDocument result = await RunAsync($"$rejected = try {{ Get-ReleaseChangelog {Ps(text)} '0.4.1' '2026-11-06' | Out-Null; $false }} catch {{ $true }}; $rejected | ConvertTo-Json");
        Assert.True(result.RootElement.GetBoolean());
    }

    [Fact]
    public async Task Changelog_ParagraphEntriesWithoutBullets_ArePreserved()
    {
        using JsonDocument result = await RunAsync($"Get-ReleaseChangelog {Ps("# Changelog\n\n## [Unreleased]\n\nRelease paragraph.")} '0.4.1' '2026-11-06' | ConvertTo-Json -Compress");
        Assert.EndsWith("Release paragraph.", result.RootElement.GetProperty("Section").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ledger_OpenAndUnknownRows_AreIncludedButPassedRowsAreExcluded()
    {
        using JsonDocument result = await RunAsync($"@(Get-OpenLargeRows {Ps(Ledger)}) | ConvertTo-Json -AsArray -Compress");
        string[] rows = result.RootElement.EnumerateArray().Select(row => row.GetString()!).ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Contains(rows, row => row.Contains("失败待修", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("部分通过", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("x \\| y", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Contains("DoneTests", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "无可解析条目")]
    [InlineData("| unrecognized | row |", "unrecognized")]
    public async Task Ledger_MissingOrMalformedData_ReportsEvidenceGap(string text, string expected)
    {
        using JsonDocument result = await RunAsync($"@(Get-OpenLargeRows {Ps(text)}) | ConvertTo-Json -AsArray -Compress");
        Assert.Contains(expected, Assert.Single(result.RootElement.EnumerateArray()).GetString()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.4.11", "v0.4.9,v0.4.10,v0.4.12-preview", true)]
    [InlineData("0.4.1", "v0.4.0", true)]
    [InlineData("0.4.2", "v0.4.0", false)]
    [InlineData("0.4.0", "v0.4.0", false)]
    [InlineData("0.5.0", "v0.4.0", true)]
    [InlineData("v0.4.1", "v0.4.0", false)]
    [InlineData("0.4.01", "v0.4.0", false)]
    [InlineData("0.4.1-preview", "v0.4.0", false)]
    [InlineData("0.4.1", "v0.3.9", false)]
    [InlineData("0.4.0", "v0.3.9", true)]
    [InlineData("0.5.1", "v0.4.0", false)]
    [InlineData("0.6.0", "v0.4.0", false)]
    [InlineData("0.4.1", "v0.5.0", false)]
    public async Task Version_RequiresCanonicalNextPatchOrMinor(string version, string tags, bool expected)
    {
        using JsonDocument result = await RunAsync($"$accepted = try {{ Assert-ReleaseVersion {Ps(version)} ({Ps(tags)}.Split(',')); $true }} catch {{ $false }}; $accepted | ConvertTo-Json");
        Assert.Equal(expected, result.RootElement.GetBoolean());
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("dirty")]
    [InlineData("ahead")]
    [InlineData("stale-version")]
    [InlineData("git-error")]
    public async Task Prepare_PreflightsBeforeWriting_UpdatesOnlyLocalFiles(string state)
    {
        using var repository = new TempRepository();
        string[] projects = ["Host", "Workbench", "Bridge.Eplan"];
        foreach (string project in projects)
        {
            repository.Write($"src/EplanMcp.{project}/EplanMcp.{project}.csproj", "<Project><PropertyGroup><Version>0.4.0</Version></PropertyGroup></Project>\n");
        }
        const string PluginPath = "packaging/claude-plugin/eplan-mcp/.claude-plugin/plugin.json";
        repository.Write("CHANGELOG.md", Changelog).Write("docs/process/large-pending.md", Ledger).Write(PluginPath, "{\"name\":\"eplan-mcp\",\"version\": \"0.4.0\"}\n");
        string command = $$"""
            function git {
                $global:LASTEXITCODE = 0
                switch ($args[2]) {
                    'status' { if ({{Ps(state)}} -eq 'dirty') { '?? untracked.txt' }; if ({{Ps(state)}} -eq 'git-error') { $global:LASTEXITCODE = 128 } }
                    'rev-parse' { if ({{Ps(state)}} -eq 'ahead' -and $args[3] -eq 'HEAD') { 'other' } else { 'main-sha' } }
                    'tag' { if ({{Ps(state)}} -eq 'stale-version') { 'v0.4.1' } else { 'v0.4.0' } }
                    default { throw "Unexpected Git operation: $args" }
                }
            }
            $prepared = try { Invoke-ReleasePrep {{Ps(repository.Root)}} '0.4.1' | Out-Null; $true } catch { $false }
            $prepared | ConvertTo-Json
            """;
        using JsonDocument result = await RunAsync(command);
        bool prepared = state == "clean";
        Assert.Equal(prepared, result.RootElement.GetBoolean());
        using JsonDocument plugin = JsonDocument.Parse(repository.Read(PluginPath));
        Assert.Equal(prepared ? "0.4.1" : "0.4.0", plugin.RootElement.GetProperty("version").GetString());
        foreach (string project in projects)
        {
            Assert.Contains($"<Version>{(prepared ? "0.4.1" : "0.4.0")}</Version>", repository.Read($"src/EplanMcp.{project}/EplanMcp.{project}.csproj"), StringComparison.Ordinal);
        }
        string notesPath = Path.Combine(repository.Root, ".local/release/v0.4.1-notes.md");
        Assert.Equal(prepared, File.Exists(notesPath));
        if (prepared)
        {
            string notes = File.ReadAllText(notesPath);
            Assert.Contains("## 未实机验证 / 默认关闭", notes, StringComparison.Ordinal);
            Assert.Contains("ApiTryTests", notes, StringComparison.Ordinal);
            Assert.DoesNotContain("DoneTests", notes, StringComparison.Ordinal);
            Assert.Contains("## [Unreleased]\n\n## [0.4.1] - ", repository.Read("CHANGELOG.md"), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(Changelog, repository.Read("CHANGELOG.md"));
        }
    }

    private static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static async Task<JsonDocument> RunAsync(string command)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "eng/scripts/release-prep.ps1")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        string script = "$ErrorActionPreference = 'Stop'; $WarningPreference = 'SilentlyContinue'; [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); . " + Ps(Path.Combine(root.FullName, "eng/scripts/release-prep.ps1")) + "; " + command;
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, await error);
        return JsonDocument.Parse(await output);
    }
}
