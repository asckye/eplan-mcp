using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class NewProjectCompatibilityTests
{
    [Fact]
    public void Compatible_ReturnsTheVersionReadFromTheNewElk()
    {
        var files = new Files(path =>
        {
            Assert.Equal(@"C:\scratch\Line1\Line1.elk", path);
            return ("IsCompatible", "18.0");
        });
        Assert.Equal("18.0", NewProjectExecution.CheckCompatibility(files, @"C:\scratch\Line1\Line1.elk"));
    }

    [Theory]
    [InlineData("IsNewer")]
    [InlineData("IsOlder")]
    [InlineData("iscompatible")]
    [InlineData("")]
    public void Incompatible_IsInvalidArgument_WithoutPromisingAnUpgrade(string status)
    {
        var files = new Files(_ => (status, "19.0"));
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => NewProjectExecution.CheckCompatibility(files, "new.elk"));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("no automatic upgrade", failure.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckFailure_IsAnOperationFailure_AndPreservesSanitizedEplanMetadata(bool businessError)
    {
        var original = new BridgeErrorException(ErrorReason.LicenceMissing, "failed");
        original.Error.Metadata = new() { ["eplanMessage"] = "EPLAN message" };
        Exception cause = businessError ? original : new InvalidOperationException(@"C:\private\detail");
        var files = new Files(_ => throw cause);
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => NewProjectExecution.CheckCompatibility(files, "new.elk"));
        Assert.Equal(ErrorReason.EplanOperationFailed, failure.Error.Reason);
        Assert.Same(cause, failure.InnerException);
        Assert.DoesNotContain("private", failure.Error.Message, StringComparison.Ordinal);
        Assert.Equal(businessError ? "EPLAN message" : null, failure.Error.Metadata?.GetValueOrDefault("eplanMessage"));
    }

    private sealed class Files(Func<string, (string Status, string DatabaseVersion)> read) : IProjectFiles
    {
        public (string Status, string DatabaseVersion) Compatibility(string linkFile) => read(linkFile);
    }
}
