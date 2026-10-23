using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>client-config's text and JSON (architecture 5.2), from fixed reports.</summary>
[Trait("Size", "Small")]
public sealed class ClientConfigOutputTests
{
    private static readonly LocalServer _server = new(@"C:\Users\工程师\EplanMcp\app\eplanmcp.exe");

    private static readonly CultureInfo _zh = CultureInfo.GetCultureInfo("zh-CN");

    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Json_HasTheDocumentedShape()
    {
        var report = new ClientConfigReport(_server, ClientConfigMode.Write, null, [new("codex", @"C:\c.toml", ClientAction.Write, ClientOutcome.Written)]);

        JsonObject json = JsonNode.Parse(ClientConfigOutput.Json(report))!.AsObject();

        Assert.Equal(["schemaVersion", "mode", "server", "problem", "clients"], json.Select(p => p.Key));
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal("""{"name":"eplan","command":"C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""", json["server"]!.ToJsonString(HostJson.Options));
        Assert.Null(json["problem"]);
        Assert.Equal(
            ["client", "file", "action", "result", "before", "after", "backup", "notes", "problem"],
            json["clients"]![0]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Json_NamesEveryModeOutcomeAndProblemThereIs()
    {
        // A new value needs a name of its own, which changes the shape (a new schemaVersion).
        Assert.Equal(["write", "dry_run", "remove"], Enum.GetValues<ClientConfigMode>().Select(m => Json(new(_server, m, null, []))["mode"]!.GetValue<string>()));
        Assert.Equal(
            ["written", "would_write", "removed", "unchanged", "failed"],
            Enum.GetValues<ClientOutcome>().Select(o => Json(Report(new("x", null, ClientAction.Write, o)))["clients"]![0]!["result"]!.GetValue<string>()));
        Assert.Equal(
            [
                "invalid_file", "unsupported_toml", "ambiguous_package", "file_changed", "invalid_record", "record_failed", "unknown_client", "io_error", "not_eplanmcp",
                "plugin_installed", "client_configured", "claude_desktop_running", "msix_redirected",
            ],
            Enum.GetValues<ClientProblem>().Select(p => Json(new(_server, ClientConfigMode.Write, new(p, "d"), []))["problem"]!["code"]!.GetValue<string>()));
        Assert.Equal(["write", "remove"], Enum.GetValues<ClientAction>().Select(a => Json(Report(new("x", null, a, ClientOutcome.Unchanged)))["clients"]![0]!["action"]!.GetValue<string>()));
    }

    [Fact]
    public void Json_GivesProblemsAndNotesInBothLanguages()
    {
        ClientResult result = new("vscode-copilot", @"C:\mcp.json", ClientAction.Write, ClientOutcome.Written)
        {
            CommentsDropped = true,
            Problem = new(ClientProblem.RecordFailed, @"D:\数据\state\clients.json: bad"),
        };

        JsonNode client = Json(Report(result))["clients"]![0]!;

        Assert.Equal("comments_dropped", client["notes"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(["code", "zh", "en"], client["problem"]!.AsObject().Select(p => p.Key));
        Assert.Contains(@"D:\数据\state\clients.json: bad", client["problem"]!["zh"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("Done, but the record", client["problem"]!["en"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_ShowsTheEntryBeforeAndAfterOnlyForAPreview()
    {
        ClientResult preview = new("cursor", @"C:\Users\工程师\.cursor\mcp.json", ClientAction.Write, ClientOutcome.WouldWrite) { After = "\"eplan\": {\n  \"command\": \"x\"\n}" };
        ClientResult written = new("codex", @"C:\c.toml", ClientAction.Write, ClientOutcome.Written) { Before = "[mcp_servers.eplan]", After = "[mcp_servers.eplan]", Backup = @"C:\c.toml.bak_1" };

        string text = ClientConfigOutput.Text(new(_server, ClientConfigMode.DryRun, null, [preview, written]), _en);

        Assert.Equal(
            """
            eplan: C:\Users\工程师\EplanMcp\app\eplanmcp.exe serve
            [would write] cursor  C:\Users\工程师\.cursor\mcp.json
                before:
                    (none)
                after:
                    "eplan": {
                      "command": "x"
                    }
            [written] codex  C:\c.toml
                backup: C:\c.toml.bak_1
            Quit the changed clients fully and open them again to load the change.

            """,
            text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Text_InChinese_NamesTheProblem()
    {
        ClientResult failed = new("claude-desktop", null, ClientAction.Write, ClientOutcome.Failed) { Problem = new(ClientProblem.AmbiguousPackage, "Claude_a, Claude_b") };

        string text = ClientConfigOutput.Text(Report(failed), _zh);

        Assert.Equal(
            """
            eplan: C:\Users\工程师\EplanMcp\app\eplanmcp.exe serve
            [失败] claude-desktop
                有多个 Claude 桌面版的包，无法确定写入哪一个：Claude_a, Claude_b

            """,
            text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Text_OfARefusedRunOrNothingRecorded_IsOneLine()
    {
        Assert.EndsWith(
            "[failed] client-config must run as eplanmcp.exe itself, whose full path the entry gets; this process is dotnet.exe\n",
            ClientConfigOutput.Text(new(_server, ClientConfigMode.Write, new(ClientProblem.NotEplanmcp, "dotnet.exe"), []), _en).ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.EndsWith("没有记录的条目，不需要移除\n", ClientConfigOutput.Text(new(_server, ClientConfigMode.Remove, null, []), _zh).ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_IsAnyProblem()
    {
        Assert.False(Report(new("x", null, ClientAction.Write, ClientOutcome.Unchanged)).Failed);
        Assert.True(Report(new("x", null, ClientAction.Write, ClientOutcome.Written) { Problem = new(ClientProblem.RecordFailed, "d") }).Failed);
        Assert.True(new ClientConfigReport(_server, ClientConfigMode.Remove, new(ClientProblem.InvalidRecord, "d"), []).Failed);
    }

    private static ClientConfigReport Report(ClientResult result) => new(_server, ClientConfigMode.Write, null, [result]);

    private static JsonNode Json(ClientConfigReport report) => JsonNode.Parse(ClientConfigOutput.Json(report))!;
}
