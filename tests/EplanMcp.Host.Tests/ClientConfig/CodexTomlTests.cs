using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>The line-by-line edit of Codex's config.toml: TIA's rules, including the forms it refuses.</summary>
[Trait("Size", "Small")]
public sealed class CodexTomlTests
{
    private const string Block = """
        [mcp_servers.eplan]
        command = "C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe"
        args = ["serve"]
        startup_timeout_sec = 120
        tool_timeout_sec = 300

        """;

    private static readonly LocalServer _server = new(@"C:\Users\工程师\EplanMcp\app\eplanmcp.exe");

    [Fact]
    public void Write_ToANewFile_IsTheTableWithTiasTimeouts()
    {
        ClientFileEdit edit = CodexToml.Write(string.Empty, _server);

        Assert.Equal(Lf(Block), edit.Text);
        Assert.Null(edit.Before);
        Assert.Equal(Lf(Block).TrimEnd(), edit.After);
    }

    [Fact]
    public void Write_AppendsAfterEverythingElseUnchanged()
    {
        string original = Lf("""
            model = "gpt-5"  # the default
            [mcp_servers.github]
            command = "npx"
            env = { GITHUB_TOKEN = "ghp_secret" }
            [mcp_servers.eplan-vm]
            url = "http://10.0.0.5:8766/mcp"


            """);

        ClientFileEdit edit = CodexToml.Write(original, _server);

        Assert.Equal(original.TrimEnd() + "\n\n" + Lf(Block), edit.Text);
        Assert.DoesNotContain("secret", edit.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OverAnExistingTable_ReplacesCommandAndArgsInPlace()
    {
        string original = Lf("""
            [mcp_servers.eplan]  # EPLAN
            command = 'D:\dev\eplanmcp.exe'   # old build
            args = ["serve", "--old"]
            startup_timeout_sec = 30

            [mcp_servers.eplan.env]
            EPLANMCP_HOME = 'D:\data'

            [profiles.work]
            model = "o3"
            """);

        ClientFileEdit edit = CodexToml.Write(original, _server);

        Assert.Equal(
            Lf("""
                [mcp_servers.eplan]  # EPLAN
                command = "C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe"
                args = ["serve"]
                startup_timeout_sec = 30

                [mcp_servers.eplan.env]
                EPLANMCP_HOME = 'D:\data'

                [profiles.work]
                model = "o3"
                """),
            edit.Text);
        Assert.Contains(@"D:\dev\eplanmcp.exe", edit.Before, StringComparison.Ordinal);
        Assert.EndsWith("EPLANMCP_HOME = 'D:\\data'", edit.After, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[mcp_servers.\"eplan\"]")]
    [InlineData("[ 'mcp_servers' . eplan ]   # quoted")]
    public void Write_FindsTheTableUnderAnyQuoting(string header)
    {
        string original = header + "\n\"command\" = \"old.exe\"\nargs = []\n";

        string text = CodexToml.Write(original, _server).Text!;

        Assert.Equal(header + "\ncommand = \"C:\\\\Users\\\\工程师\\\\EplanMcp\\\\app\\\\eplanmcp.exe\"\nargs = [\"serve\"]\n", text);
    }

    [Fact]
    public void Write_ToATableWithoutLaunchFields_AddsThemToThatTable()
    {
        string original = Lf("""
            [mcp_servers.eplan]
            enabled = true

            [other]
            x = 1
            """);

        string text = CodexToml.Write(original, _server).Text!;

        Assert.Equal(
            Lf("""
                [mcp_servers.eplan]
                enabled = true

                command = "C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe"
                args = ["serve"]
                [other]
                x = 1
                """),
            text);
    }

    [Fact]
    public void Write_WhenTheTableIsCurrent_LeavesTheFileAlone()
    {
        string current = CodexToml.Write("model = \"o3\"\n", _server).Text!;

        ClientFileEdit edit = CodexToml.Write(current, _server);

        Assert.Null(edit.Text);
        Assert.Equal(edit.Before, edit.After);
        Assert.StartsWith("[mcp_servers.eplan]", edit.Before, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_KeepsCrlfLineEnds()
    {
        string original = "[mcp_servers.eplan]\r\ncommand = \"old.exe\"\r\nargs = []\r\n\r\n[x]\r\ny = 1\r\n";

        string text = CodexToml.Write(original, _server).Text!;

        Assert.Equal(text.Split('\n').Length - 1, text.Split("\r\n").Length - 1);
        Assert.EndsWith("[x]\r\ny = 1\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_IgnoresLookalikesInStringsAndComments()
    {
        string original = Lf("""
            notes = '''
            [mcp_servers.eplan]
            mcp_servers = 1
            '''
            # [mcp_servers.eplan]
            title = "[mcp_servers.eplan] # not a table"
            """);

        string text = CodexToml.Write(original, _server).Text!;

        Assert.Equal(original + "\n\n" + Lf(Block), text);
    }

    [Theory]
    [InlineData("mcp_servers = { eplan = { command = \"x\" } }")]
    [InlineData("mcp_servers.eplan.command = \"x\"")]
    [InlineData("\"mcp_servers\".other = { command = \"x\" }")]
    [InlineData("[mcp_servers]\neplan = { command = \"x\" }")]
    [InlineData("[mcp_servers]\neplan.command = \"x\"")]
    [InlineData("[mcp_servers]\n'eplan' = { command = \"x\" }")]
    [InlineData("[[mcp_servers.eplan]]\ncommand = \"x\"")]
    [InlineData("[mcp_servers.eplan]\ncommand = \"a\"\n[mcp_servers.eplan]\ncommand = \"b\"")]
    [InlineData("[mcp_servers.eplan]\ncommand = \"a\"\ncommand = \"b\"")]
    [InlineData("[mcp_servers.eplan]\ncommand = \"\"\"x.exe\"\"\"")]
    [InlineData("[mcp_servers.eplan]\ncommand = ['x.exe']")]
    [InlineData("[mcp_servers.eplan]\nargs = [\n  \"serve\",\n]")]
    [InlineData("[mcp_servers.eplan]\nargs = [\"\"\"serve\"\"\"]")]
    [InlineData("[mcp_servers.eplan]\nargs = [\"a\"]\nargs = [\"b\"]")]
    public void Write_RefusesWhatALineEditCannotChangeSafely(string original)
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => CodexToml.Write(original, _server));

        Assert.Equal(ClientProblem.UnsupportedToml, refused.Problem);
        Assert.StartsWith("Line ", refused.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_WithOnlySubTablesOfTheServer_IsRefusedRatherThanLosingThem()
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => CodexToml.Write("[mcp_servers.eplan.env]\nA = \"1\"\n", _server));

        Assert.Equal(ClientProblem.UnsupportedToml, refused.Problem);
    }

    [Fact]
    public void LinesOfAMultiLineArray_AreNeverTables()
    {
        string original = Lf("""
            [mcp_servers.eplan]
            command = "old.exe"
            env_vars = [
              ["A", "B"],
              ["C", "D"]
            ]
            startup_timeout_sec = 30

            [other]
            y = 2
            """);

        string written = CodexToml.Write(original, _server).Text!;
        ClientFileEdit removed = CodexToml.Remove(original, LocalServer.Name);

        Assert.Equal(
            Lf("""
                [mcp_servers.eplan]
                command = "C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe"
                env_vars = [
                  ["A", "B"],
                  ["C", "D"]
                ]
                startup_timeout_sec = 30

                args = ["serve"]
                [other]
                y = 2
                """),
            written);
        Assert.Equal("[other]\ny = 2\n", removed.Text);
        Assert.EndsWith("startup_timeout_sec = 30", removed.Before, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("title = \"not closed")]
    [InlineData("title = 'not closed")]
    [InlineData("notes = \"\"\"\nnot closed")]
    [InlineData("notes = '''\nnot closed")]
    [InlineData("list = [1, 2\n")]
    [InlineData("point = { x = 1\n")]
    public void Write_ToAFileWithAnOpenString_IsRefusedAsInvalid(string original)
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => CodexToml.Write(original, _server));

        Assert.Equal(ClientProblem.InvalidFile, refused.Problem);
    }

    [Fact]
    public void Write_AcceptsEscapedQuotesAndOtherServersFreely()
    {
        string original = Lf(""""
            path = "C:\\x\"y\\"
            notes = """
            multi \""" quote
            """
            [mcp_servers.eplanx]
            command = "a"
            [mcp_servers.other]
            eplan = 1
            """");

        string text = CodexToml.Write(original, _server).Text!;

        Assert.StartsWith(original, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_TakesOutTheTableAndItsSubTablesOnly()
    {
        string original = Lf("""
            model = "o3"

            [mcp_servers.eplan]
            command = "C:\\eplanmcp.exe"
            args = ["serve"]

            [mcp_servers.eplan.env]
            A = "1"

            [mcp_servers.github]
            command = "npx"
            """);

        ClientFileEdit edit = CodexToml.Remove(original, LocalServer.Name);

        Assert.Equal(Lf("""
            model = "o3"

            [mcp_servers.github]
            command = "npx"

            """), edit.Text);
        Assert.StartsWith("[mcp_servers.eplan]", edit.Before, StringComparison.Ordinal);
        Assert.EndsWith("A = \"1\"", edit.Before, StringComparison.Ordinal);
        Assert.Null(edit.After);
    }

    [Fact]
    public void Remove_ANamedServer_LeavesTheOthers()
    {
        string original = "[mcp_servers.eplan]\ncommand = \"a\"\n[mcp_servers.eplan-vm]\nurl = \"http://10.0.0.5:8766/mcp\"\n";

        Assert.Equal("[mcp_servers.eplan]\ncommand = \"a\"\n", CodexToml.Remove(original, "eplan-vm").Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("model = \"o3\"\n")]
    [InlineData("[mcp_servers.eplanx]\ncommand = \"a\"\n")]
    public void Remove_WithoutTheServer_LeavesTheFileAlone(string original)
    {
        Assert.Equal(new ClientFileEdit(null, null, null), CodexToml.Remove(original, LocalServer.Name));
    }

    [Fact]
    public void Remove_OfTheOnlyTable_LeavesAnEmptyFile()
    {
        Assert.Equal(string.Empty, CodexToml.Remove(Lf(Block), LocalServer.Name).Text);
    }

    [Fact]
    public void Remove_RefusesTheSameFormsAsWrite()
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => CodexToml.Remove("[mcp_servers]\neplan = { command = \"x\" }\n", LocalServer.Name));

        Assert.Equal(ClientProblem.UnsupportedToml, refused.Problem);
    }

    [Fact]
    public void Remove_AfterWrite_GivesBackTheFile()
    {
        const string Original = "model = \"o3\"\n\n[mcp_servers.github]\ncommand = \"npx\"\n";

        string written = CodexToml.Write(Original, _server).Text!;

        Assert.Equal(Original, CodexToml.Remove(written, LocalServer.Name).Text);
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n");
}
