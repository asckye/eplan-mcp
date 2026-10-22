using System.Text;
using System.Text.Json.Nodes;
using EplanMcp.Host.Packs;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Tests.Packs;

/// <summary><c>eplanmcp pack …</c> end to end on synthetic files in a temporary folder, the data root included.</summary>
[Trait("Size", "Medium")]
public sealed class PackCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-pack-cli-").FullName;

    [Fact]
    public async Task ScanWriteCheckExportInstall_EachSucceeds()
    {
        string input = Inputs();
        string pack = Path.Combine(_root, "pack");
        string file = Path.Combine(_root, "acme.eplanpack");

        (int scan, JsonNode scanned) = await RunAsync(new PackRequest(PackAction.Scan, [input], pack, new PackIdentity("acme.base", "ACME base", "1.0.0")));
        (int check, JsonNode checkedPack) = await RunAsync(new PackRequest(PackAction.Check, [pack]));
        (int export, _) = await RunAsync(new PackRequest(PackAction.Export, [pack, file]));
        (int checkFile, _) = await RunAsync(new PackRequest(PackAction.Check, [file]));
        (int install, JsonNode installed) = await RunAsync(new PackRequest(PackAction.Install, [file]));
        (int again, JsonNode reinstalled) = await RunAsync(new PackRequest(PackAction.Install, [pack]));

        Assert.Equal([0, 0, 0, 0, 0, 0], new[] { scan, check, export, checkFile, install, again });
        Assert.Equal((1, "scan"), ((int)scanned["schemaVersion"]!, (string?)scanned["command"]));
        Assert.Equal(Path.GetFullPath(pack), (string?)scanned["written"]);
        Assert.Equal("acme.base", (string?)scanned["manifest"]!["id"]);
        Assert.Equal(2, (int)checkedPack["resources"]!);
        Assert.Equal("installed", (string?)installed["outcome"]);
        Assert.Equal("already_installed", (string?)reinstalled["outcome"]);
        Assert.True(Directory.Exists(Path.Combine(_root, "home", "packs", "acme.base", "1.0.0")));
    }

    [Fact]
    public async Task Scan_ProjectWithoutDataFolder_ExitsWith1AndNamesTheFinding()
    {
        string input = Inputs();
        File.WriteAllText(Path.Combine(input, "Lonely.elk"), "Lonely.edb\n18.0\n");

        (int exit, JsonNode result) = await RunAsync(new PackRequest(PackAction.Scan, [input], Path.Combine(_root, "pack"), new PackIdentity("acme.base", "ACME base", "1.0.0")));

        Assert.Equal(1, exit);
        Assert.False((bool)result["ok"]!);
        Assert.Contains(result["findings"]!.AsArray(), f => (string?)f!["code"] == "missing_data_folder" && (string?)f["severity"] == "error");
        Assert.False(Directory.Exists(Path.Combine(_root, "pack")));
    }

    [Fact]
    public async Task Scan_InvalidIdentity_IsAProblem()
    {
        (int exit, JsonNode result) = await RunAsync(new PackRequest(PackAction.Scan, [Inputs()], null, new PackIdentity("ACME", "ACME base", "1.0")));

        Assert.Equal(1, exit);
        Assert.Equal(["id", "version"], result["problems"]!.AsArray().Select(p => (string?)p!["location"]));
    }

    [Fact]
    public async Task Text_NamesResourcesAndTheOutcome()
    {
        using var output = new StringWriter();

        int exit = await PackCommand.RunAsync(new PackRequest(PackAction.Scan, [Inputs()]), new HostDataLocations(Path.Combine(_root, "home")), output, json: false, TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("macro             resources/macros/inputs/Motor.ema  (1 file(s), ", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("No errors. Give --out <folder> with --id, --name and --version to write the pack.", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        // Installed packs are read-only.
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private string Inputs()
    {
        string input = Path.Combine(_root, "inputs");
        Directory.CreateDirectory(Path.Combine(input, "Plant.edb"));
        File.WriteAllText(Path.Combine(input, "Plant.elk"), "Plant.edb\n18.0\n");
        File.WriteAllText(Path.Combine(input, "Plant.edb", "Page.eod"), "pages");
        File.WriteAllText(Path.Combine(input, "Motor.ema"), "<EplanPxfRoot Type=\"WindowMacro\" Version=\"2026.0.25356\" />", new UTF8Encoding(false));
        return input;
    }

    private async Task<(int Exit, JsonNode Result)> RunAsync(PackRequest request)
    {
        using var output = new StringWriter();
        int exit = await PackCommand.RunAsync(request, new HostDataLocations(Path.Combine(_root, "home")), output, json: true, TestContext.Current.CancellationToken);
        return (exit, JsonNode.Parse(output.ToString())!);
    }
}
