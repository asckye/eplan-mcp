using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ModuleInspectArgumentsTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"project\":1,\"macro\":\"x\",\"sha256\":\"x\",\"pages\":[]}")]
    [InlineData("{\"project\":\"x\",\"macro\":\"x\",\"sha256\":\"x\",\"pages\":[1]}")]
    [InlineData("{\"project\":\"x\",\"macro\":\"x\",\"sha256\":\"x\",\"pages\":[],\"apply\":false}")]
    public void ParseArgs_UnknownOrCoercedMembers_AreRejected(string json)
    {
        var operation = new ModuleInspectOperation(null!, () => throw new InvalidOperationException("No native calls allowed."));
        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.ParseArgs(JToken.Parse(json))).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("/:", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("macro")]
    [InlineData("hash")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("many")]
    [InlineData("path")]
    [InlineData("null")]
    public void Execute_InvalidShapeOrRange_StopsBeforeLicenceAndReader(string invalid)
    {
        var args = new ModuleInspectArgs { Project = @"C:\reference\Copy.elk", Macro = @"C:\pack\Module.ema", Sha256 = new string('a', 64), Pages = ["+A1/1"] };
        switch (invalid)
        {
            case "project":
                args.Project = @"\\server\Copy.elk";
                break;
            case "macro":
                args.Macro = @"C:\pack\..\Module.ema";
                break;
            case "hash":
                args.Sha256 = new string('A', 64);
                break;
            case "empty":
                args.Pages.Clear();
                break;
            case "duplicate":
                args.Pages.Add("+A1/1");
                break;
            case "many":
                args.Pages = [.. Enumerable.Range(0, 51).Select(i => "+A1/" + i)];
                break;
            case "path":
                args.Pages = [@"C:\private"];
                break;
            case "null":
                args.Pages = null!;
                break;
        }
        var operation = new ModuleInspectOperation(null!, () => throw new InvalidOperationException("No native calls allowed."));
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.Execute(args)).Error.Reason);
    }
}
