using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed partial class ApiTryOperationTests
{
    private const string PartVariant = "O:Eplan.EplApi.MasterData.MDPart.Variant";

    private static ApiTryArgs PartArgs(string? value) => new()
    {
        Group = PartVariant,
        Project = "Scratch",
        Parameters = new JObject
        {
            ["target"] = new JObject { ["handle"] = "h:" + new string('a', 32), ["type"] = "Eplan.EplApi.MasterData.MDPart" },
        }.Also(p => { if (value is not null) { p["value"] = value; } }).ToString(Newtonsoft.Json.Formatting.None),
    };

    [Fact]
    public void MasterDataWrite_NamesTheConfiguredFileDatabase_AndItsSettingKeysTheFingerprint()
    {
        _dispatch.Configured = new() { Database = @"$(MD_PARTS)\Copy.mdb", Path = @"D:\scratch\parts\Copy.mdb" };
        WritePlan plan = Plan(PartArgs("2"));
        MasterDataTarget target = Assert.IsType<MasterDataTarget>(plan.MasterData);
        Assert.Equal(MasterDataKinds.Parts, target.Kind);
        Assert.Equal(@"D:\scratch\parts\Copy.mdb", target.Path);
        Assert.False(target.Server);
        _dispatch.Configured = new() { Database = @"$(MD_PARTS)\Other.mdb", Path = @"D:\scratch\parts\Other.mdb" };
        Assert.NotEqual(plan.Fingerprint, Plan(PartArgs("2")).Fingerprint);
    }

    [Fact]
    public void MasterDataRead_UsesAnyDatabase()
    {
        _dispatch.Configured = new() { Database = "Server|Parts|0" };
        Assert.Null(Plan(PartArgs(null)).MasterData);
    }

    [Theory]
    [InlineData("Server|Parts|0", "")]
    [InlineData("", "")]
    [InlineData(@"$(UNKNOWN)\Parts.mdb", "")]
    public void MasterDataWrite_WithoutAResolvableFileDatabase_IsDeniedBeforeRunning(string database, string path)
    {
        _dispatch.Configured = new() { Database = database, Path = path };
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Plan(PartArgs("2")));
        Assert.Equal(ErrorReason.PolicyDenied, error.Error.Reason);
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
    }
}

internal static class JObjectExtensions
{
    internal static JObject Also(this JObject value, Action<JObject> change)
    {
        change(value);
        return value;
    }
}
