using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class MacroStoreNameTests
{
    [Theory]
    [InlineData("")]
    [InlineData("../motor")]
    [InlineData("..\\motor")]
    [InlineData("C:macro")]
    [InlineData("a b")]
    [InlineData("电机")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("motor.")]
    [InlineData("CON")]
    [InlineData("nul.ema")]
    [InlineData("m\n")]
    public async Task InvalidNameNeverCallsBridge(string name)
    {
        var bridge = new FakeBridgeClient();
        var input = new JsonObject { ["project"] = "Line1", ["operation"] = "page", ["pages"] = new JsonArray("=A/1"), ["name"] = name };
        CallToolResult result = await MacroStoreTools.RunAsync(bridge, new HostDataLocations("unused"), input, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Empty(bridge.Calls);
    }

    [Theory]
    [InlineData("\"bad\"", "false")]
    [InlineData("\"page\"", "\"false\"")]
    [InlineData("null", "true")]
    public async Task InvalidOperationOrPreviewFlagNeverCallsBridge(string operation, string dry)
    {
        var bridge = new FakeBridgeClient();
        JsonObject input = JsonNode.Parse($$"""{"project":"Line1","name":"macro","operation":{{operation}},"dry_run":{{dry}}} """)!.AsObject();
        Assert.True((await MacroStoreTools.RunAsync(bridge, new HostDataLocations("unused"), input, TestContext.Current.CancellationToken)).IsError);
        Assert.Empty(bridge.Calls);
    }
}

[Trait("Size", "Medium")]
public sealed class MacroStoreFileTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-macro-store-").FullName;
    private readonly FakeBridgeClient _bridge = new();
    private HostDataLocations Locations => new(_temp);
    private string Target => Path.Combine(Locations.Exports, "macros", "motor.ema");
    private static JsonObject Input => JsonNode.Parse("""{"project":"Line1","operation":"window","page":"=A/1","name":"motor","locators":[{"project":"Line1","page":"=A/1","type":1,"oid":123,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]} """)!.AsObject();

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private void Reply(Action<JsonObject>? action = null) => _bridge.Handlers[OperationNames.MacroStore] = value =>
    {
        var args = (JsonObject)value!;
        action?.Invoke(args);
        return new JsonObject { ["project"] = "Line1", ["path"] = args["path"]!.DeepClone(), ["fingerprint"] = "planned", ["changes"] = new JsonArray(), ["variants"] = new JsonArray(0) };
    };

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task PreviewDoesNotCreateAnyFileOrDirectory(bool? dry)
    {
        Reply();
        JsonObject input = Input;
        if (dry.HasValue)
        { input["dry_run"] = dry.Value; }
        CallToolResult result = await MacroStoreTools.RunAsync(_bridge, Locations, input, TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp));
        Assert.Single(_bridge.Calls);
        JsonElement data = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(ExecutionState.NotStarted, data.GetProperty("execution").GetString());
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Contains("dry_run", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("window", ".ema")]
    [InlineData("page", ".emp")]
    public async Task ExecutionPublishesVerifiedFileWithSizeHashAndFingerprint(string operation, string extension)
    {
        byte[] content = [1, 2, 3, 4];
        Reply(args =>
        {
            if (!args["dry_run"]!.GetValue<bool>())
            {
                Assert.Equal("planned", args["fingerprint"]!.GetValue<string>());
                File.WriteAllBytes(args["path"]!.GetValue<string>(), content);
            }
        });
        JsonObject input = Input;
        input["operation"] = operation;
        if (operation == "page")
        {
            input.Remove("locators");
            input.Remove("page");
            input["pages"] = new JsonArray("=A/1");
        }
        input["dry_run"] = false;
        CallToolResult result = await MacroStoreTools.RunAsync(_bridge, Locations, input, TestContext.Current.CancellationToken);
        Assert.NotEqual(true, result.IsError);
        JsonElement data = JsonSerializer.SerializeToElement(result.StructuredContent);
        string path = data.GetProperty("path").GetString()!;
        Assert.EndsWith(extension, path, StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.Equal(content.Length, data.GetProperty("size").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), data.GetProperty("sha256").GetString());
        Assert.Equal(ExecutionState.Committed, data.GetProperty("execution").GetString());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingDestinationIsRefusedBeforeBridge(bool dry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.WriteAllText(Target, "original");
        JsonObject input = Input;
        input["dry_run"] = dry;
        Assert.True((await MacroStoreTools.RunAsync(_bridge, Locations, input, TestContext.Current.CancellationToken)).IsError);
        Assert.Empty(_bridge.Calls);
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("bridge")]
    [InlineData("collision")]
    public async Task FailedExportCleansOnlyItsTemporaryFile(string mode)
    {
        Reply(args =>
        {
            if (args["dry_run"]!.GetValue<bool>())
            { return; }
            string path = args["path"]!.GetValue<string>();
            if (mode != "missing")
            { File.WriteAllText(path, mode == "empty" ? "" : "macro"); }
            if (mode == "collision")
            { File.WriteAllText(Target, "other export"); }
            if (mode == "bridge")
            { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.EplanOperationFailed, Message = "Readback failed" }); }
        });
        JsonObject input = Input;
        input["dry_run"] = false;
        CallToolResult result = await MacroStoreTools.RunAsync(_bridge, Locations, input, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        Assert.Equal(mode == "collision" ? [Target] : [], Directory.GetFiles(Path.GetDirectoryName(Target)!));
        if (mode == "collision")
        { Assert.Equal("other export", File.ReadAllText(Target)); }
    }

    [Fact]
    public async Task TimeoutRemovesThePrivateFolderBeforeALateWrite()
    {
        string? pendingPath = null;
        Reply(args =>
        {
            if (args["dry_run"]!.GetValue<bool>())
            { return; }
            pendingPath = args["path"]!.GetValue<string>();
            throw new BridgeCallException(new BridgeError { Reason = ErrorReason.Timeout, Message = "Native export still running" });
        });
        JsonObject input = Input;
        input["dry_run"] = false;
        Assert.True((await MacroStoreTools.RunAsync(_bridge, Locations, input, TestContext.Current.CancellationToken)).IsError);
        Assert.NotNull(pendingPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(pendingPath)));
        Assert.Throws<DirectoryNotFoundException>(() => File.WriteAllText(pendingPath, "late"));
        Assert.False(File.Exists(Target));
    }
}
